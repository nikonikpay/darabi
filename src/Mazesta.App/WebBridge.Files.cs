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
