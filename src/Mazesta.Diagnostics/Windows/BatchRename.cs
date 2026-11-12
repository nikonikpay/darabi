using System.Security.Cryptography; using System.Text.Json; using Mazesta.Core.Windows;
namespace Mazesta.Diagnostics.Windows;

/// <summary>
/// A batch rename carried out, and its undo. Every file first takes a temporary name and then its new one, so names can swap ("a" to "b"
/// and "b" to "a"); a file that fails is put back. What was done is written to a log before the page is told, so "undo the last rename"
/// works after the app restarts, and renames a file back only if it still has the name it was given.
/// </summary>
public static class BatchRename
{
    public sealed record Step(string From, string To);
    public sealed record Outcome(int Renamed, IReadOnlyList<(string Path, string Error)> Failed);

    public static Outcome Apply(IReadOnlyList<RenameItem> items, string undoLog)
    {
        var ready = items.Where(i => i.State == RenameState.Ready).ToList();
        var moved = new List<(string Temp, RenameItem Item)>(); var failed = new List<(string, string)>(); var done = new List<Step>();
        foreach (var i in ready)
        {
            string temp = Path.Combine(Path.GetDirectoryName(i.Path)!, $".mz-rename-{Guid.NewGuid():N}{Path.GetExtension(i.Path)}");
            try { File.Move(i.Path, temp); moved.Add((temp, i)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add((i.Path, ex.Message)); }
        }
        foreach (var (temp, i) in moved)
        {
            string to = Path.Combine(Path.GetDirectoryName(i.Path)!, i.NewName);
            try { File.Move(temp, to); done.Add(new(i.Path, to)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add((i.Path, ex.Message)); TryMove(temp, i.Path); }
        }
        Write(undoLog, done);
        return new(done.Count, failed);
    }

    /// <summary>The last batch put back, newest last-in first-out; each file only if it still has the new name and its old one is free.</summary>
    public static Outcome Undo(string undoLog)
    {
        var steps = Read(undoLog); var failed = new List<(string, string)>(); int back = 0;
        // through temporary names too, so a swap goes back; an old name taken by a file outside the batch is left alone
        var batch = steps.Select(s => s.To).ToHashSet(StringComparer.OrdinalIgnoreCase); var moved = new List<(string Temp, Step Step)>();
        foreach (var s in steps)
        {
            if (!File.Exists(s.To)) { failed.Add((s.To, "gone")); continue; }
            if (File.Exists(s.From) && !batch.Contains(s.From)) { failed.Add((s.To, "taken")); continue; }
            string temp = Path.Combine(Path.GetDirectoryName(s.To)!, $".mz-rename-{Guid.NewGuid():N}{Path.GetExtension(s.To)}");
            if (TryMove(s.To, temp)) moved.Add((temp, s)); else failed.Add((s.To, "refused"));
        }
        foreach (var (temp, s) in moved)
            if (TryMove(temp, s.From)) back++; else { failed.Add((s.To, "refused")); TryMove(temp, s.To); }
        File.Delete(undoLog);
        return new(back, failed);
    }

    public static IReadOnlyList<Step> Read(string undoLog)
    {
        try { return File.Exists(undoLog) ? JsonSerializer.Deserialize<List<Step>>(File.ReadAllText(undoLog)) ?? [] : []; }
        catch (JsonException) { return []; }
    }

    private static void Write(string undoLog, List<Step> done)
    {
        if (done.Count == 0) return;
        Directory.CreateDirectory(Path.GetDirectoryName(undoLog)!);
        File.WriteAllText(undoLog, JsonSerializer.Serialize(done));
    }

    private static bool TryMove(string from, string to) { try { File.Move(from, to); return true; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; } }
}

public static class FileHash
{
    /// <summary>MD5, SHA-1 and SHA-256 of a file in one read (a big ISO is read once), with the share read so far.</summary>
    public static (string Md5, string Sha1, string Sha256) Hash(string path, Action<double> progress, CancellationToken ct)
    {
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5); using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1); using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
        var buf = new byte[1 << 20]; long read = 0, len = Math.Max(1, f.Length), next = Environment.TickCount64; int n;
        while ((n = f.Read(buf, 0, buf.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            md5.AppendData(buf, 0, n); sha1.AppendData(buf, 0, n); sha256.AppendData(buf, 0, n); read += n;
            if (Environment.TickCount64 >= next) { progress((double)read / len); next = Environment.TickCount64 + 250; }
        }
        return (Convert.ToHexStringLower(md5.GetHashAndReset()), Convert.ToHexStringLower(sha1.GetHashAndReset()), Convert.ToHexStringLower(sha256.GetHashAndReset()));
    }
}
