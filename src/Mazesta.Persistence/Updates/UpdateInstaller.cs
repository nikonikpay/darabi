using System.IO.Compression;
namespace Mazesta.Persistence.Updates;

/// <summary>
/// Puts a new release in place of the app's files, the way <c>tools/publish.ps1</c> does, and never touches <c>Data</c> (settings, reports,
/// history). The old files are moved aside (to <c>Data/cache/update/previous</c>, the same drive, so it is a rename) and the new ones copied in;
/// if anything fails half-way, what was copied is removed and the old files are moved back, so the app is either wholly old or wholly new.
/// </summary>
public static class UpdateInstaller
{
    public const string ExeName = "MazestaWeb.exe";

    /// <summary>Unpacks a downloaded release into an empty <paramref name="staging"/> folder and checks it is one (the app's exe at its top, no Data).</summary>
    public static void Extract(string zip, string staging)
    {
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        ZipFile.ExtractToDirectory(zip, staging);   // refuses entries that would land outside the folder
        if (!File.Exists(Path.Combine(staging, ExeName))) throw new InvalidDataException($"The package has no {ExeName} at its top.");
        if (Directory.Exists(Path.Combine(staging, AppPaths.DataFolderName))) throw new InvalidDataException("The package must not contain a Data folder.");
    }

    /// <summary>Replaces everything in <paramref name="appDir"/> but Data with <paramref name="staging"/>'s files. <paramref name="log"/> gets each step.
    /// Returns false (the old files back in place) when it could not be done.</summary>
    public static bool Apply(string staging, string appDir, string previous, Action<string> log)
    {
        if (Directory.Exists(previous)) Directory.Delete(previous, true);
        Directory.CreateDirectory(previous);
        var old = Entries(appDir).ToList(); var moved = new List<string>();
        try
        {
            foreach (var path in old)
            {
                Retry(() => Move(path, Path.Combine(previous, Path.GetFileName(path))));
                moved.Add(Path.GetFileName(path));
            }
            log($"Moved {moved.Count} old entries aside");
            CopyTree(staging, appDir);
            log("Copied the new release in");
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"Update failed, restoring the old files: {e.Message}");
            var unmoved = old.Select(Path.GetFileName).Except(moved, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);   // still the old files: kept
            foreach (var path in Entries(appDir).Where(p => !unmoved.Contains(Path.GetFileName(p)))) try { Delete(path); } catch (Exception x) when (x is IOException or UnauthorizedAccessException) { log($"Could not remove {path}: {x.Message}"); }
            foreach (var name in moved) try { Move(Path.Combine(previous, name), Path.Combine(appDir, name)); } catch (Exception x) when (x is IOException or UnauthorizedAccessException) { log($"Could not restore {name}: {x.Message}"); }
            return false;
        }
    }

    private static IEnumerable<string> Entries(string dir) => Directory.EnumerateFileSystemEntries(dir).Where(p => !string.Equals(Path.GetFileName(p), AppPaths.DataFolderName, StringComparison.OrdinalIgnoreCase));

    private static void CopyTree(string from, string to)
    {
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(from, file);
            if (rel.StartsWith(AppPaths.DataFolderName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            Retry(() => File.Copy(file, Path.Combine(to, rel), overwrite: true));
        }
    }

    private static void Move(string from, string to) { if (Directory.Exists(from)) Directory.Move(from, to); else File.Move(from, to); }
    private static void Delete(string path) { if (Directory.Exists(path)) Directory.Delete(path, true); else File.Delete(path); }

    /// <summary>A file the old process (or an antivirus scan) still holds for a moment is tried again for a few seconds before giving up.</summary>
    private static void Retry(Action action)
    {
        for (int i = 0; ; i++)
        {
            try { action(); return; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && i < 20) { Thread.Sleep(500); }
        }
    }
}
