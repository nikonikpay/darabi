using System.Text.Json; using Mazesta.Core.Windows; using Mazesta.Desktop.Localization; using Mazesta.Diagnostics.Windows; using Microsoft.Extensions.Logging;
namespace Mazesta.App;

/// <summary>The file tools of the Windows tools page (what holds a file open, ...). A path reaches them only from a dialog the user picked it in.</summary>
public sealed partial class WebBridge
{
    private readonly HashSet<string> _picked = new(StringComparer.OrdinalIgnoreCase);

    private void RegisterFiles()
    {
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
                using var f = new System.Windows.Forms.OpenFileDialog { Title = Loc.Get("Files_PickFile"), CheckFileExists = true, Multiselect = kind != "one", Filter = filter, DereferenceLinks = true };
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
        Method("rename.preview", p =>
        {
            var (items, error) = RenameItems(p);
            return new { error, items = items.Select(i => new { name = Path.GetFileName(i.Path), newName = i.NewName, state = i.State }),
                ready = items.Count(i => i.State == RenameState.Ready), blocked = items.Count(i => i.State is RenameState.Clash or RenameState.Bad) };
        });
        MethodAsync("rename.apply", async p =>
        {
            var (items, error) = RenameItems(p);
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

        // ——— Where the disk space went: one scan at a time, its progress pushed as "space"; the last result is kept for browsing until the next ———
        Method("space.drives", _ => DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => new { root = d.Name, label = d.VolumeLabel, total = d.TotalSize, free = d.AvailableFreeSpace }).ToList());
        Method("space.scan", p =>
        {
            string root = Str(p, "root");
            bool drive = DriveInfo.GetDrives().Any(d => d.DriveType == DriveType.Fixed && d.IsReady && string.Equals(d.Name, root, StringComparison.OrdinalIgnoreCase));
            if (!drive && !(_picked.Contains(root) && Directory.Exists(root))) throw new InvalidOperationException("not picked");
            if (_spaceCts is not null) throw new InvalidOperationException(Loc.Get("Files_Space_Busy"));
            var cts = _spaceCts = new CancellationTokenSource(); _space = null;
            _ = Task.Run(() =>
            {
                try
                {
                    var r = SpaceScan.Scan(root, x => Push("space", new { busy = true, files = x.Files, bytes = x.Bytes, folder = x.Folder }), cts.Token);
                    _window.Dispatcher.BeginInvoke(() => { _space = r; _spaceCts = null; Push("space", new { busy = false, done = true }); });
                    _log.LogInformation("Space scan of {Root}: {Files} files, {Bytes} bytes", root, r.Root.Files, r.Root.Size);
                }
                catch (OperationCanceledException) { _window.Dispatcher.BeginInvoke(() => { _spaceCts = null; Push("space", new { busy = false, cancelled = true }); }); }
                catch (Exception ex) { _log.LogWarning(ex, "Space scan of {Root} failed", root); _window.Dispatcher.BeginInvoke(() => { _spaceCts = null; Push("space", new { busy = false, error = ex.Message }); }); }
            });
            return null;
        });
        Method("space.cancel", _ => { _spaceCts?.Cancel(); return null; });
        // A folder of the last scan, by its names under the root: its biggest folders (the rest summed) and, at the root, the biggest files.
        Method("space.list", p =>
        {
            if (_space is not { } s) return null;
            var at = p.ValueKind == JsonValueKind.Object && p.TryGetProperty("at", out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [];
            var n = s.Root.Find(at) ?? throw new InvalidOperationException("gone");
            object Row(SpaceNode c) => new { name = c.Name, size = c.Size, files = c.Files, unread = c.Unread, partUnread = c.PartUnread };
            long inFolders = n.Children.Sum(c => c.Size);
            return new
            {
                path = n.FullPath, at, size = n.Size, files = n.Files, unread = n.Unread, partUnread = n.PartUnread, here = n.Size - inFolders,
                children = n.Children.Take(40).Select(Row), others = n.Children.Count > 40 ? n.Children.Skip(40).Sum(c => c.Size) : 0,
                top = at.Count == 0 ? s.Top.Biggest().Select(f => new { path = f.Path, size = f.Size }) : null,
            };
        });
        // Explorer at a folder of the scan, or with one of its biggest files selected: only paths the scan itself found.
        Method("space.open", p =>
        {
            if (_space is not { } s) return null;
            string file = Str(p, "file");
            string? arg = file.Length > 0 ? s.Top.Biggest().Any(f => f.Path == file) && File.Exists(file) ? $"/select,\"{file}\"" : null
                : s.Root.Find(p.TryGetProperty("at", out var a) ? a.EnumerateArray().Select(x => x.GetString() ?? "") : []) is { } n && Directory.Exists(n.FullPath) ? $"\"{n.FullPath}\"" : null;
            if (arg is null) throw new InvalidOperationException("gone");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), arg) { UseShellExecute = false })?.Dispose();
            return null;
        });
    }

    private (SpaceNode Root, TopFiles Top)? _space; private CancellationTokenSource? _spaceCts, _hashCts;
    private string RenameLog => Path.Combine(_paths.DataRoot, "rename", "last.json");

    /// <summary>The batch's files (a picked folder stands for the files directly in it, by name) and the rule the page sent, planned here.</summary>
    private (IReadOnlyList<RenameItem> Items, string? Error) RenameItems(JsonElement p)
    {
        var files = Picked(p).SelectMany(x => Directory.Exists(x) ? Directory.EnumerateFiles(x).Order(StringComparer.CurrentCultureIgnoreCase) : (IEnumerable<string>)[x]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var rule = new RenameRule(Str(p, "find"), Str(p, "replace"), Bool(p, "regex"), Bool(p, "matchCase"), Bool(p, "number"), int.TryParse(Str(p, "start"), out int s) && s >= 0 ? s : 1);
        return RenamePlan.Check(rule) is { } bad ? ([], bad) : (RenamePlan.Make(files, rule, File.Exists), null);
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
