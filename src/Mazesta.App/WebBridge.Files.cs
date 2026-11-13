using System.Text.Json; using System.Windows.Forms; using Mazesta.Core.Windows; using Mazesta.Desktop.Localization; using Mazesta.Diagnostics.Windows; using Microsoft.Extensions.Logging; using Mazesta.Core.Tray;
namespace Mazesta.App;

/// <summary>The file tools of the Windows tools page (what holds a file open, ...). A path reaches them only from a dialog the user picked it in.</summary>
public sealed partial class WebBridge
{
    private readonly HashSet<string> _picked = new(StringComparer.OrdinalIgnoreCase);

    private void RegisterFiles()
    {
        // Explorer's right-click entries for these tools start the tray, with no UAC prompt (see ShellFiles); made at every start, so a moved copy or another language is put right
        string tray = Path.Combine(AppContext.BaseDirectory, "MazestaTray.exe");
        if (File.Exists(tray))
            try { FileShellMenu.Set(tray, tool => Loc.Get("Files_Menu_" + tool)); }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { _log.LogWarning(e, "The file tools' right-click entries were not added"); }
        // the tray has gathered a selection from Explorer: the window comes forward and the page takes the files (files.pending)
        var shell = new EventWaitHandle(false, EventResetMode.AutoReset, ShellFiles.Event);
        var wait = ThreadPool.RegisterWaitForSingleObject(shell, (_, _) => _window.Dispatcher.BeginInvoke(() => { _window.BringForward(); Push("shellfiles", null); }), null, Timeout.Infinite, false);
        _cleanup.Add(() => { wait.Unregister(null); shell.Dispose(); });
        Method("files.pending", _ => TakeShellFiles());

        // A dialog for files (several) or a folder; an empty list when the user closes it. What was picked is remembered, and the tools below act only on it.
        Method("files.pick", p =>
        {
            string kind = Str(p, "kind"); var owner = Desktop.Composition.UiDispatcher.Owner; List<string> paths = [];
            if (kind == "folder")
            {
                using var d = new System.Windows.Forms.FolderBrowserDialog { Description = Loc.Get("Files_PickFolder"), UseDescriptionForTitle = true, ShowNewFolderButton = false };
                if (d.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK) paths.Add(d.SelectedPath);
            }
            else
            {
                string filter = kind == "images" ? Loc.Get("Files_Filter_Images") + "|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|" + Loc.Get("Shield_Pick_AnyFilter") + "|*.*" : Loc.Get("Shield_Pick_AnyFilter") + "|*.*";
                // ValidateNames off (FOS_NOVALIDATE): the dialog otherwise refuses a file another program holds open, the very file the lock box is for
                using var f = new System.Windows.Forms.OpenFileDialog { Title = Loc.Get("Files_PickFile"), CheckFileExists = true, ValidateNames = false, Multiselect = kind != "one", Filter = filter, DereferenceLinks = true };
                if (f.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK) paths.AddRange(f.FileNames);
            }
            foreach (var x in paths) _picked.Add(x);
            return paths;
        });

        MethodAsync("locks.find", async p =>
        {
            var paths = Picked(p);
            var r = await Task.Run(() => FileLocks.Find(paths)).ConfigureAwait(true);
            if (r.Error is not null) _log.LogWarning("File locks of {Count} paths: {Error}", paths.Count, r.Error);
            return new { holders = r.Holders.Select(Holder), checkedFiles = r.FilesChecked, truncated = r.Truncated, cap = FileLocks.FolderCap, error = r.Error };
        });
        // Closes a holder only if Windows still reports it for these paths, so the page cannot end any other process.
        MethodAsync("locks.close", async p =>
        {
            var paths = Picked(p); int pid = int.TryParse(Str(p, "pid"), out int i) ? i : 0; long start = long.TryParse(Str(p, "start"), out long s) ? s : 0;
            var h = await Task.Run(() => FileLocks.Recheck(paths, pid, start)).ConfigureAwait(true);
            string? error = h is null ? "gone" : await Task.Run(() => FileLocks.Close(h)).ConfigureAwait(true);
            _log.LogInformation("Closed file holder {Name} ({Pid}): {Result}", h?.Name, pid, error ?? "ended");
            return new { error };
        });

        // ——— Pictures made smaller: a new file beside each picked one, one at a time, each reported as it is done ("imgdone") ———
        MethodAsync("img.resize", async p =>
        {
            var paths = Picked(p);
            int longest = int.TryParse(Str(p, "size"), out int n) && n is >= 16 and <= 20000 ? n : throw new InvalidOperationException(Loc.Get("Files_Img_BadSize"));
            int quality = int.TryParse(Str(p, "quality"), out int q) ? q : 85;
            int ok = 0;
            foreach (var (path, i) in paths.Select((x, i) => (x, i)))
            {
                var r = await Task.Run(() => Desktop.Services.ImageResizer.Resize(path, longest, quality)).ConfigureAwait(true);
                if (r.Output is not null) { ok++; _picked.Add(r.Output); }
                Push("imgdone", new { index = i, total = paths.Count, name = Path.GetFileName(path), output = r.Output is null ? null : Path.GetFileName(r.Output),
                    width = r.Width, height = r.Height, newWidth = r.NewWidth, newHeight = r.NewHeight, oldBytes = r.OldBytes, newBytes = r.NewBytes, error = r.Error });
            }
            _log.LogInformation("Resized {Ok} of {Count} pictures to {Longest} px", ok, paths.Count, longest);
            return new { ok, total = paths.Count, folder = paths.Count > 0 ? Path.GetDirectoryName(paths[0]) : null };
        });

        // ——— Many files renamed: the plan is made here from the rule (never from names the page sends), shown, then carried out as shown ———
        // off the window's thread: a folder of thousands of files is listed and checked on every keystroke
        MethodAsync("rename.preview", async p =>
        {
            var paths = Picked(p);
            var (items, error) = await Task.Run(() => RenameItems(p, paths)).ConfigureAwait(true);
            return new { error, items = items.Select(i => new { name = Path.GetFileName(i.Path), newName = i.NewName, state = i.State }),
                ready = items.Count(i => i.State == RenameState.Ready), blocked = items.Count(i => i.State is RenameState.Clash or RenameState.Bad) };
        });
        MethodAsync("rename.apply", async p =>
        {
            var paths = Picked(p);
            var (items, error) = await Task.Run(() => RenameItems(p, paths)).ConfigureAwait(true);
            if (error is not null || items.Any(i => i.State is RenameState.Clash or RenameState.Bad)) throw new InvalidOperationException(Loc.Get("Files_Ren_Blocked"));
            var r = await Task.Run(() => BatchRename.Apply(items, RenameLog)).ConfigureAwait(true);
            foreach (var i in items.Where(i => i.State == RenameState.Ready)) _picked.Add(Path.Combine(Path.GetDirectoryName(i.Path)!, i.NewName));
            _log.LogInformation("Renamed {Count} files, {Failed} failed", r.Renamed, r.Failed.Count);
            return new { renamed = r.Renamed, failed = r.Failed.Select(f => new { name = Path.GetFileName(f.Path), error = f.Error }) };
        });
        Method("rename.last", _ => new { count = BatchRename.Read(RenameLog).Count });
        MethodAsync("rename.undo", async _ =>
        {
            var r = await Task.Run(() => BatchRename.Undo(RenameLog)).ConfigureAwait(true);
            _log.LogInformation("Rename undone: {Count} back, {Failed} not", r.Renamed, r.Failed.Count);
            return new { renamed = r.Renamed, failed = r.Failed.Select(f => new { name = Path.GetFileName(f.Path), error = f.Error }) };
        });

        // ——— A file's checksum: MD5, SHA-1 and SHA-256 in one read, the progress pushed as "hash" ———
        MethodAsync("hash.file", async p =>
        {
            var path = Picked(p).FirstOrDefault() ?? throw new InvalidOperationException("no files");
            _hashCts?.Cancel(); var cts = _hashCts = new CancellationTokenSource();
            try
            {
                var (md5, sha1, sha256) = await Task.Run(() => FileHash.Hash(path, x => Push("hash", new { share = x }), cts.Token)).ConfigureAwait(true);
                return new { name = Path.GetFileName(path), size = new FileInfo(path).Length, md5, sha1, sha256 };
            }
            catch (OperationCanceledException) { return new { cancelled = true }; }
            finally { if (_hashCts == cts) _hashCts = null; }
        });
        Method("hash.cancel", _ => { _hashCts?.Cancel(); return null; });
        Method("hash.compare", p => new { kind = Checksum.KindOf(Str(p, "text")), clean = Checksum.Clean(Str(p, "text")) });

        // ——— A colour from anywhere on the screen: the tray's picker (it works with the app closed, and shows the pick in a window of its own) ———
        Method("color.pick", _ => new { open = SignalTray(Mazesta.Core.Tray.FileSearchSignals.ColorPick) });

        // ——— The quick file search lives in the tray (its index and window): the app only asks the tray to show it ———
        Method("search.open", _ => new { on = _config.FileSearch, key = Mazesta.Core.Tray.Hotkey.Normalize(_config.FileSearchKey) ?? Mazesta.Core.Tray.FileSearchSignals.DefaultKey,
            open = _config.FileSearch && SignalTray(Mazesta.Core.Tray.FileSearchSignals.Show) });
        // each program's volume: the panel lives in the tray (it works with the app closed too)
        Method("mixer.open", _ => new { open = SignalTray(Mazesta.Core.Tray.FileSearchSignals.Mixer) });
        Method("search.state", _ => new { on = _config.FileSearch, key = Mazesta.Core.Tray.Hotkey.Normalize(_config.FileSearchKey) ?? Mazesta.Core.Tray.FileSearchSignals.DefaultKey,
            tray = System.Diagnostics.Process.GetProcessesByName(Mazesta.Core.Tray.OverlaySignals.TrayProcess).Length > 0 });

        // ——— Where the disk space went: a window of the tray's (an NTFS drive read from its file table in seconds), so it works with the app closed too ———
        Method("space.open", _ => new { open = SignalTray(Mazesta.Core.Tray.FileSearchSignals.Space) });
    }

    /// <summary>Sets one of the tray's named events; false when no tray has made it (none running).</summary>
    internal static bool SignalTray(string name)
    {
        if (!EventWaitHandle.TryOpenExisting(name, out var e)) return false;
        using (e) e.Set();
        return true;
    }

    private CancellationTokenSource? _hashCts;

    /// <summary>
    /// The files Explorer's right-click entries sent (through the tray), taken once: each tool's paths that still exist, remembered as picked, so the
    /// box acts on them as on files chosen in its own dialog - and still only when the user presses its button.
    /// </summary>
    private object TakeShellFiles()
    {
        string file = Path.Combine(_paths.DataRoot, "tray", ShellFiles.FileName); List<ShellFiles.Request> list;
        try
        {
            if (!File.Exists(file)) return Array.Empty<object>();
            list = JsonSerializer.Deserialize<List<ShellFiles.Request>>(File.ReadAllText(file)) ?? []; File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { _log.LogWarning(e, "The right-click request could not be read"); return Array.Empty<object>(); }
        var ok = list.Where(r => ShellFiles.Tools.Contains(r.Tool))
            .Select(r => new { tool = r.Tool, paths = r.Paths.Where(x => Path.IsPathFullyQualified(x) && (File.Exists(x) || Directory.Exists(x))).ToList() }).Where(r => r.paths.Count > 0).ToList();
        foreach (var r in ok) foreach (var x in r.paths) _picked.Add(x);
        _log.LogInformation("Right-click request: {Requests}", string.Join(", ", ok.Select(r => $"{r.tool} {r.paths.Count}")));
        return ok;
    }
    private string RenameLog => Path.Combine(_paths.DataRoot, "rename", "last.json");

    /// <summary>The batch's files (a picked folder stands for the files directly in it, by name) and the rule the page sent, planned here.</summary>
    private static (IReadOnlyList<RenameItem> Items, string? Error) RenameItems(JsonElement p, List<string> picked)
    {
        var files = picked.SelectMany(x => Directory.Exists(x) ? Directory.EnumerateFiles(x).Order(StringComparer.CurrentCultureIgnoreCase) : (IEnumerable<string>)[x]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var rule = new RenameRule(Str(p, "find"), Str(p, "replace"), Bool(p, "regex"), Bool(p, "matchCase"), Bool(p, "number"), int.TryParse(Str(p, "start"), out int s) && s >= 0 ? s : 1);
        if (RenamePlan.Check(rule) is { } bad) return ([], bad);
        try { return (RenamePlan.Make(files, rule, File.Exists), null); }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { return ([], Loc.Get("Files_Ren_Slow")); }
    }

    private static object Holder(FileLockHolder h) => new { pid = h.Pid, start = h.StartTime.ToString(System.Globalization.CultureInfo.InvariantCulture), name = h.Name, path = h.Path, kind = h.Kind, canClose = h.CanClose };

    /// <summary>The page's paths, each one the user picked in a dialog this session (or inside a picked folder); anything else is refused.</summary>
    private List<string> Picked(JsonElement p)
    {
        if (p.ValueKind != JsonValueKind.Object || !p.TryGetProperty("paths", out var a) || a.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("no files");
        var list = a.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
        foreach (var x in list)
            if (!_picked.Contains(x) && !_picked.Any(r => Directory.Exists(r) && Path.GetFullPath(x).StartsWith(Path.TrimEndingDirectorySeparator(r) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("not picked");
        return list;
    }
}
