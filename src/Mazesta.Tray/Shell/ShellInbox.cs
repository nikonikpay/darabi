using System.Diagnostics; using System.Text.Json; using Mazesta.Core.Tray;
namespace Mazesta.Tray.Shell;

/// <summary>
/// Explorer's right-click entries for the file tools, in two halves (see <see cref="ShellFiles"/>). <see cref="Send"/> is what Explorer starts, once per
/// file and without administrator rights: it drops the path in the user's inbox folder and ends (the first of a selection also starts the tray if it is
/// not running). The tray's instance watches that folder - Windows tells it when a file arrives, nothing polls - and a second after the last one it
/// hands the selection to the app as one request. Only paths that exist are passed on; the app shows them in its box and acts only when asked there.
/// </summary>
internal sealed class ShellInbox : IDisposable
{
    private readonly FileSystemWatcher? _watch; private readonly System.Windows.Forms.Timer _quiet = new() { Interval = 1000 };
    private readonly Control _ui = new(); private readonly string _request; private readonly Action _wakeApp;

    public ShellInbox(string requestFile, Action wakeApp)
    {
        _request = requestFile; _wakeApp = wakeApp; _ui.CreateControl();
        _quiet.Tick += (_, _) => { _quiet.Stop(); Collect(); };
        try
        {
            var dir = Directory.CreateDirectory(ShellFiles.Inbox);
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) return;   // a junction put there would point the tray's deletes elsewhere
            _watch = new FileSystemWatcher(dir.FullName, "*.req") { NotifyFilter = NotifyFilters.FileName };
            _watch.Created += (_, _) => Later(); _watch.Renamed += (_, _) => Later();
            _watch.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return; }
        _quiet.Start();   // what arrived before the tray started (the handoff starts it)
    }

    // a selection's files come one start at a time: the request goes a second after the last
    private void Later() { try { _ui.BeginInvoke(() => { _quiet.Stop(); _quiet.Start(); }); } catch (InvalidOperationException) { } }

    private void Collect()
    {
        var byTool = new Dictionary<string, List<string>>();
        try
        {
            foreach (var f in new DirectoryInfo(ShellFiles.Inbox).EnumerateFiles("*.req"))
            {
                try
                {
                    if ((f.Attributes & FileAttributes.ReparsePoint) == 0 && DateTime.UtcNow - f.CreationTimeUtc < TimeSpan.FromMinutes(5))
                    {
                        var lines = File.ReadAllLines(f.FullName);
                        if (lines.Length == 2 && ShellFiles.Tools.Contains(lines[0]) && Path.IsPathFullyQualified(lines[1]) && (File.Exists(lines[1]) || Directory.Exists(lines[1])))
                            (byTool.TryGetValue(lines[0], out var l) ? l : byTool[lines[0]] = []).Add(lines[1]);
                    }
                    f.Delete();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
        if (byTool.Count == 0) return;
        var all = Read();
        all.AddRange(byTool.Select(x => new ShellFiles.Request(x.Key, [.. x.Value.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase)])));
        try { Directory.CreateDirectory(Path.GetDirectoryName(_request)!); File.WriteAllText(_request, JsonSerializer.Serialize(all)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
        _wakeApp();
    }

    // a request the app has not taken yet (it was closed, or still starting) is kept, and this one added
    private List<ShellFiles.Request> Read()
    {
        try { return File.Exists(_request) ? JsonSerializer.Deserialize<List<ShellFiles.Request>>(File.ReadAllText(_request)) ?? [] : []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }

    public void Dispose() { _watch?.Dispose(); _quiet.Dispose(); _ui.Dispose(); }

    /// <summary>
    /// Explorer's start for one file (no administrator rights, no window): the path dropped in the inbox, written whole before it gets the name the
    /// tray watches for. The first start of a selection also starts the tray (one UAC prompt) when it is not running, and waits for it to come up.
    /// </summary>
    public static void Send(string tool, string path)
    {
        if (!ShellFiles.Tools.Contains(tool)) return;
        try
        {
            var dir = Directory.CreateDirectory(ShellFiles.Inbox).FullName;
            string tmp = Path.Combine(dir, $"{tool}-{Environment.ProcessId}-{Stopwatch.GetTimestamp()}.tmp");
            File.WriteAllLines(tmp, [tool, path]); File.Move(tmp, Path.ChangeExtension(tmp, ".req"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
        using var leader = new Mutex(true, @"Local\Mazesta.ShellHandoff", out bool first);
        if (!first || TrayRuns()) return;
        try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { return; }   // the UAC prompt was declined
        for (int i = 0; i < 120 && !TrayRuns(); i++) Thread.Sleep(250);   // the selection's other starts see this one leading and only drop their paths
    }

    // The tray holds its single-instance mutex while it runs; made by an elevated process, it may refuse this one, which still says it exists.
    private static bool TrayRuns()
    {
        try { using var m = Mutex.OpenExisting(@"Local\MazestaTray"); return true; }
        catch (WaitHandleCannotBeOpenedException) { return false; }
        catch (UnauthorizedAccessException) { return true; }
    }
}
