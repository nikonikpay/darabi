using System.Text; using System.Text.Json; using System.Text.Json.Nodes;
namespace Mazesta.Persistence;

/// <summary>
/// The app's own log (Data/logs/mazesta-web-*.log) and Windows' crash records of Mazesta, sent with the usage statistics (same switch) so a problem on a computer
/// whose user never sends a diagnostics file can still be found. Only what was not sent yet, a piece at a time; the profile folder's path and the computer's
/// name are taken out first. Where it got to is kept in Data/config/logsend.json, and a piece is marked sent only after the site took it.
/// </summary>
public sealed class LogShipper(string logsDir, string configDir)
{
    private const int MaxBytes = 192 * 1024;
    private string StateFile => Path.Combine(configDir, "logsend.json");
    private sealed record State(string File, long Offset, DateTime Wer);
    public sealed record Piece(string File, string Text, JsonArray Crashes, Action Commit);

    private State Load()
    {
        try { if (File.Exists(StateFile) && JsonSerializer.Deserialize<State>(File.ReadAllText(StateFile)) is { File: not null } s) return s; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return new("mazesta-web-" + DateTime.Now.ToString("yyyyMMdd") + ".log", 0, DateTime.Now.AddDays(-7));   // first time: today's log and the last week's crashes, not every old file
    }
    private void Save(State s) { try { File.WriteAllText(StateFile, JsonSerializer.Serialize(s)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }

    /// <summary>The next piece to send, or null when nothing is new.</summary>
    public Piece? Next()
    {
        var st = Load(); string file = st.File; long offset = st.Offset; string text = ""; long end = offset;
        var files = Directory.Exists(logsDir) ? Directory.GetFiles(logsDir, "mazesta-web-*.log").Select(Path.GetFileName).OfType<string>().Where(f => string.CompareOrdinal(f, st.File) >= 0).Order(StringComparer.Ordinal).ToList() : [];
        foreach (var f in files)
        {
            long from = f == st.File ? st.Offset : 0;
            using var fs = new FileStream(Path.Combine(logsDir, f), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length <= from) { if (f != files[^1]) continue; file = f; offset = end = Math.Min(from, fs.Length); break; }
            fs.Position = from; var buf = new byte[(int)Math.Min(MaxBytes, fs.Length - from)]; fs.ReadExactly(buf);
            int cut = Array.LastIndexOf(buf, (byte)'\n') + 1; if (cut == 0) { file = f; offset = end = from; break; }   // only half a line so far: next time
            file = f; offset = from; end = from + cut; text = Scrub(Encoding.UTF8.GetString(buf, 0, cut).TrimStart('﻿')); break;
        }
        var (crashes, newest) = Crashes(st.Wer);
        if (text.Length == 0 && crashes.Count == 0) { if (file != st.File || end != st.Offset) Save(st with { File = file, Offset = end }); return null; }
        return new(file, text, crashes, () => Save(new(file, end, newest)));
    }

    /// <summary>The paths under the user's profile and the computer's name, taken out of the text.</summary>
    public static string Scrub(string text)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), machine = Environment.MachineName;
        if (profile.Length > 3) text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        if (machine.Length > 2) text = text.Replace(machine, "%COMPUTERNAME%", StringComparison.OrdinalIgnoreCase);
        return text;
    }

    /// <summary>Windows Error Reporting's records of Mazesta's own crashes newer than <paramref name="after"/>: the faulting module, the exception and where.</summary>
    private static (JsonArray, DateTime) Crashes(DateTime after)
    {
        var list = new JsonArray(); var newest = after;
        string wer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER");
        foreach (var dir in new[] { "ReportArchive", "ReportQueue" }.Select(d => Path.Combine(wer, d)).Where(Directory.Exists))
        {
            try
            {
                foreach (var d in Directory.GetDirectories(dir, "AppCrash_Mazesta*"))
                {
                    var report = Path.Combine(d, "Report.wer"); if (!File.Exists(report)) continue;
                    var at = File.GetLastWriteTime(report); if (at <= after) continue;
                    var sig = new JsonObject { ["at"] = at.ToUniversalTime().ToString("O") };
                    foreach (var line in File.ReadAllLines(report, Encoding.Unicode))
                        if (line.StartsWith("Sig[", StringComparison.Ordinal) && line.Contains(".Value=", StringComparison.Ordinal)) sig["sig" + line[4..line.IndexOf(']')]] = Scrub(line[(line.IndexOf('=') + 1)..]);
                    list.Add(sig); if (at > newest) newest = at;
                    if (list.Count >= 20) return (list, newest);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return (list, newest);
    }
}
