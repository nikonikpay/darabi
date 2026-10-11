using System.Diagnostics; using System.IO; using System.IO.Compression; using System.Text;
namespace Mazesta.App;

/// <summary>What Windows itself recorded about crashes, for the diagnostics export: a process that dies natively leaves nothing in the app's own log, but Windows keeps its event, its error report and sometimes a dump.</summary>
internal static class WindowsEvidence
{
    private const int Days = 14, MaxDumps = 3; private const long MaxDumpBytes = 64L << 20;
    private const string Window = "TimeCreated[timediff(@SystemTime) <= 1209600000]";

    // Application: critical and error events (app crashes 1000, hangs 1002, .NET runtime 1026, WER 1001). System: critical and error, plus the display drivers' warnings (a reset is a warning, event 4101) and WHEA.
    private static readonly (string Log, string Query)[] Queries =
    [
        ("Application", $"*[System[(Level=1 or Level=2) and {Window}]]"),
        ("System", $"*[System[(Level=1 or Level=2 or Provider[@Name='Display' or @Name='nvlddmkm' or @Name='amdkmdag' or @Name='amdwddmg' or @Name='igfx' or @Name='Microsoft-Windows-WHEA-Logger']) and {Window}]]"),
    ];

    public static void AddTo(ZipArchive archive)
    {
        var texts = Queries.Select(q => Task.Run(() => Events(q.Log, q.Query))).ToArray();
        foreach (var (q, t) in Queries.Zip(texts)) Put(archive, $"windows/events-{q.Log}.txt", t.GetAwaiter().GetResult());
        Put(archive, "windows/dumps.txt", Dumps(archive));
    }

    private static string Events(string log, string query)
    {
        try
        {
            var psi = new ProcessStartInfo("wevtutil.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
            foreach (var a in new[] { "qe", log, "/q:" + query, "/f:text", "/rd:true", "/c:300" }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEndAsync();
            var outp = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(30_000)) { try { p.Kill(true); } catch (InvalidOperationException) { } return $"wevtutil did not finish in 30 s{Environment.NewLine}"; }
            return $"# {log}, last {Days} days, newest first{Environment.NewLine}{outp.GetAwaiter().GetResult()}{err.GetAwaiter().GetResult()}";
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException) { return $"wevtutil failed: {e.Message}{Environment.NewLine}"; }
    }

    /// <summary>The newest Windows error reports (small text files) and kernel minidumps (copied when not large); what was left out is listed, never skipped silently.</summary>
    private static string Dumps(ZipArchive archive)
    {
        var sb = new StringBuilder();
        try
        {
            string wer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER");
            foreach (var sub in new[] { "ReportArchive", "ReportQueue" })
            {
                string root = Path.Combine(wer, sub); if (!Directory.Exists(root)) { sb.AppendLine($"{root}: not there"); continue; }
                foreach (var d in new DirectoryInfo(root).EnumerateDirectories().OrderByDescending(x => x.LastWriteTime).Take(15))
                {
                    string f = Path.Combine(d.FullName, "Report.wer");
                    if (File.Exists(f)) { sb.AppendLine($"{d.LastWriteTime:yyyy-MM-dd HH:mm} {sub}/{d.Name}"); Copy(archive, f, $"windows/wer/{sub}-{d.Name}.wer"); }
                }
            }
            string mini = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");
            var dumps = new List<FileInfo>();
            if (Directory.Exists(mini)) dumps.AddRange(new DirectoryInfo(mini).EnumerateFiles("*.dmp").OrderByDescending(x => x.LastWriteTime));
            string live = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "LiveKernelReports");   // GPU timeout (TDR) dumps
            if (Directory.Exists(live)) dumps.AddRange(new DirectoryInfo(live).EnumerateFiles("*.dmp", SearchOption.AllDirectories).OrderByDescending(x => x.LastWriteTime));
            int kept = 0;
            foreach (var f in dumps.OrderByDescending(x => x.LastWriteTime))
            {
                bool take = kept < MaxDumps && f.Length <= MaxDumpBytes;
                sb.AppendLine($"{f.LastWriteTime:yyyy-MM-dd HH:mm} {f.FullName} {f.Length / 1024} KiB {(take ? "(in the zip)" : f.Length > MaxDumpBytes ? "(too large, not copied)" : "(older, not copied)")}");
                if (take) { Copy(archive, f.FullName, $"windows/dumps/{f.Name}"); kept++; }
            }
            if (dumps.Count == 0) sb.AppendLine("No kernel dumps found (Minidump, LiveKernelReports).");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { sb.AppendLine($"Reading the dump folders failed: {e.Message}"); }
        return sb.ToString();
    }

    private static void Copy(ZipArchive archive, string file, string name)
    {
        try
        {
            using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var dst = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
            src.CopyTo(dst);
        }
        catch (IOException) { }
    }

    private static void Put(ZipArchive archive, string name, string text) { using var w = new StreamWriter(archive.CreateEntry(name, CompressionLevel.Optimal).Open(), Encoding.UTF8); w.Write(text); }
}
