using System.Diagnostics; using System.IO; using System.Net.Http; using System.Reflection;
using Mazesta.Persistence; using Mazesta.Persistence.Updates;
using Microsoft.Extensions.Logging;
namespace Mazesta.App;

public enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Ready, Installing, Failed }

/// <summary>
/// The app's own updates, like other programs have: it reads the signed manifest from the shop's site (<see cref="Folder"/>), fetches new benchmark
/// comparison lists on its own (they are data, small and checked), and downloads and installs a new release when the user asks. Installing hands
/// over to the new release's own exe, run from where it was unpacked with <see cref="ApplyArgument"/>: it waits for this process to end, puts its
/// files in place of the old ones (Data is never touched; see <see cref="UpdateInstaller"/>) and starts the app again. One per process: the
/// window's page can be made again, the update in progress must not be lost with it.
/// </summary>
public sealed class AppUpdater
{
    /// <summary>The update folder on the shop's site. Everything the app fetches for updates is under it: update.json, update.json.sig, the zips, benchdb/.</summary>
    public static readonly Uri Folder = new("https://www.dfmrendering.com/mazesta/");
    /// <summary>The shop's update-signing public key (ECDSA P-256). The private key is kept outside the repository (see docs/UPDATES.md).</summary>
    internal const string PublicKey = UpdateKey.Public;
    public const string ApplyArgument = "--apply-update", UpdatedArgument = "--updated";

    /// <summary>The shop's site plugin (Mazesta Connect): what the app sends to the site, and the comparison lists built there.</summary>
    public static readonly Uri Api = new("https://www.dfmrendering.com/wp-json/mazesta/v1/");
    // An hour: the release is one self-contained package of some hundred megabytes, and a slow line is not a failure.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(60), DefaultRequestHeaders = { { "User-Agent", "MazestaTest/1.0 (+https://www.dfmrendering.com)" } } };
    private static AppUpdater? s_instance;
    private readonly AppPaths _paths; private readonly ILogger _log; private readonly UpdateClient _client;
    private readonly SemaphoreSlim _busy = new(1, 1);
    private bool _autoChecked;

    public static AppUpdater Get(AppPaths paths, ILogger log) => s_instance ??= new AppUpdater(paths, log);

    private AppUpdater(AppPaths paths, ILogger log)
    {
        _paths = paths; _log = log; _client = new UpdateClient(Folder, PublicKey, Http);
        Current = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
        Task.Run(CleanUp);
    }

    public string Current { get; }
    public UpdateState State { get; private set; } = UpdateState.Idle;
    public UpdateManifest? Manifest { get; private set; }
    public double Progress { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset? CheckedAt { get; private set; }
    public DateTimeOffset? DataSyncedAt { get; private set; }
    /// <summary>How many comparison lists the last sync fetched (0 when all were current).</summary>
    public int DataDownloaded { get; private set; }
    public event Action? Changed;
    /// <summary>New comparison lists are on disk.</summary>
    public event Action? DataChanged;

    /// <summary>The lists the comparisons read: the site's own where it has any, the signed update folder's otherwise.</summary>
    public int DataLists => DataSync.Count(_paths.BenchSiteDir) is > 0 and var site ? site : DataSync.Count(_paths.BenchDbDir);
    public SiteClient Site { get; } = new(Api, Http);

    /// <summary>The site's comparison lists, fetched where they changed. A site without the plugin (or out of reach) is no error: the lists on
    /// disk stay. True when something changed on disk.</summary>
    public async Task<bool> SyncSiteListsAsync()
    {
        try
        {
            var r = await Site.SyncListsAsync(_paths.BenchSiteDir, CancellationToken.None).ConfigureAwait(false);
            DataSyncedAt = DateTimeOffset.Now;
            if (r.Downloaded + r.Removed == 0) return false;
            _log.LogInformation("Site comparison lists: {Down} downloaded, {Removed} removed, {Total} in all", r.Downloaded, r.Removed, r.Total);
            DataDownloaded += r.Downloaded; DataChanged?.Invoke();
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or SiteException or InvalidDataException)
        {
            _log.LogInformation("Site comparison lists not fetched: {Message}", e.Message);
            return false;
        }
    }
    private string Staging(string version) => Path.Combine(_paths.UpdateDir, version, "app");

    /// <summary>Once per run of the app, a while after start-up: new lists are fetched, a new release is only reported. Nothing is said when offline.</summary>
    public void AutoCheck()
    {
        if (_autoChecked || _paths.Flash) return;   // a flash-drive copy is replaced by hand, not by itself
        _autoChecked = true;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            while (Quiet?.Invoke() == true) await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);   // not during a benchmark
            await CheckAsync(quiet: true).ConfigureAwait(false);
        });
    }

    /// <summary>True while the app is measuring the machine: the check on its own waits until it is done (one asked for still runs).</summary>
    public Func<bool>? Quiet { get; set; }

    public async Task CheckAsync(bool quiet = false)
    {
        if (!await _busy.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            if (State is UpdateState.Ready) return;   // unpacked and waiting for Install
            Set(UpdateState.Checking);
            DataDownloaded = 0;
            await SyncSiteListsAsync().ConfigureAwait(false);   // first, and on its own: it does not depend on a release being published
            var m = await _client.CheckAsync(CancellationToken.None).ConfigureAwait(false);
            Manifest = m; CheckedAt = DateTimeOffset.Now;
            _log.LogInformation("Update check: the site offers {Version} (this is {Current}), {Lists} data files", m.App?.Version ?? "no release", Current, m.Data.Count);
            try
            {
                var synced = await DataSync.SyncAsync(_client, m, _paths.BenchDbDir, CancellationToken.None).ConfigureAwait(false);
                DataSyncedAt = DateTimeOffset.Now; DataDownloaded += synced.Downloaded;
                if (synced.Downloaded + synced.Removed > 0) { _log.LogInformation("Comparison lists: {Down} downloaded, {Removed} removed, {Total} in all", synced.Downloaded, synced.Removed, synced.Total); DataChanged?.Invoke(); }
            }
            // A list missing on the site must not hide a release that is there: the lists on disk stay, the release is still offered.
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException) { _log.LogInformation("Signed comparison lists not fetched: {Message}", e.Message); }
            Set(m.IsNewer(Current) ? UpdateState.Available : UpdateState.UpToDate);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or UpdateRejectedException or InvalidDataException)
        {
            _log.LogInformation("Update check failed: {Message}", e.Message);
            if (quiet) Set(UpdateState.Idle); else Fail(e);
        }
        finally { _busy.Release(); }
    }

    /// <summary>True while only the comparison lists are being fetched (<see cref="SyncDataAsync"/>).</summary>
    public bool DataBusy { get; private set; }

    /// <summary>Only the benchmark comparison lists, asked for by the user: the release state and its error are left alone (a manifest the shop
    /// did not sign must not show up as a failed app update when the user asked for data).</summary>
    public async Task SyncDataAsync()
    {
        if (!await _busy.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            DataBusy = true; DataDownloaded = 0; Changed?.Invoke();
            await SyncSiteListsAsync().ConfigureAwait(false);
            try
            {
                var m = await _client.CheckAsync(CancellationToken.None).ConfigureAwait(false);
                var synced = await DataSync.SyncAsync(_client, m, _paths.BenchDbDir, CancellationToken.None).ConfigureAwait(false);
                DataSyncedAt = DateTimeOffset.Now; DataDownloaded += synced.Downloaded;
                if (synced.Downloaded + synced.Removed > 0) DataChanged?.Invoke();
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or UpdateRejectedException or InvalidDataException)
            { _log.LogInformation("Signed comparison lists not fetched: {Message}", e.Message); }
        }
        finally { DataBusy = false; _busy.Release(); Changed?.Invoke(); }
    }

    /// <summary>Downloads and unpacks the offered release. It is not installed until <see cref="Install"/>.</summary>
    public async Task DownloadAsync()
    {
        if (Manifest?.App is not { } release || !Manifest.IsNewer(Current) || !await _busy.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            Progress = 0; Set(UpdateState.Downloading);
            string zip = Path.Combine(_paths.UpdateDir, release.Version + ".zip");
            double last = 0;
            await _client.DownloadAsync(release.Package, zip, f => { Progress = f; if (f - last >= 0.01) { last = f; Changed?.Invoke(); } }, CancellationToken.None).ConfigureAwait(false);
            await Task.Run(() => UpdateInstaller.Extract(zip, Staging(release.Version))).ConfigureAwait(false);
            File.Delete(zip);
            _log.LogInformation("Release {Version} downloaded and unpacked", release.Version);
            Set(UpdateState.Ready);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _log.LogWarning("Release download failed: {Message}", e.Message); Fail(e);
        }
        finally { _busy.Release(); }
    }

    /// <summary>Starts the unpacked release to put itself in place, and returns true: the caller then ends the app so its files are free.</summary>
    public bool Install()
    {
        if (State != UpdateState.Ready || Manifest?.App is not { } release) return false;
        string exe = Path.Combine(Staging(release.Version), UpdateInstaller.ExeName);
        if (!File.Exists(exe)) { Fail(new FileNotFoundException("The unpacked release is missing.", exe)); return false; }
        string appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        psi.ArgumentList.Add(ApplyArgument); psi.ArgumentList.Add(appDir); psi.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _log.LogInformation("Installing {Version}: handing over to {Exe}", release.Version, exe);
        Process.Start(psi)?.Dispose();
        Set(UpdateState.Installing);
        return true;
    }

    private void Set(UpdateState s) { State = s; if (s != UpdateState.Failed) Error = null; Changed?.Invoke(); }
    private void Fail(Exception e) { Error = e.Message; State = UpdateState.Failed; Changed?.Invoke(); }

    /// <summary>What an earlier update left behind, now that the release it unpacked is running: the unpacked copies and zips (the replaced files,
    /// "previous", stay until the next update, to go back by hand if a release turns out bad).</summary>
    private void CleanUp()
    {
        try
        {
            if (!Directory.Exists(_paths.UpdateDir)) return;
            foreach (var d in Directory.EnumerateDirectories(_paths.UpdateDir).Where(d => Path.GetFileName(d) != "previous")) Directory.Delete(d, true);
            foreach (var f in Directory.EnumerateFiles(_paths.UpdateDir)) File.Delete(f);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log.LogInformation("Update leftovers not removed: {Message}", e.Message); }
    }

    /// <summary>
    /// The new release, started from where it was unpacked: waits for the old app to end, stops the tray (its exe is one of the files) and starts it
    /// again afterwards, puts the files in place and starts the app. Written to Data/logs/update.log, since there is no window to say anything in.
    /// </summary>
    public static void Apply(string appDir, int pid, Action releaseInstance)
    {
        string staging = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var paths = AppPaths.Create(appDir);
        string logFile = Path.Combine(paths.LogsDir, "update.log");
        void Log(string line) { try { Directory.CreateDirectory(paths.LogsDir); File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}"); } catch (IOException) { } }
        Log($"Applying {staging} to {appDir}");
        try { using var old = Process.GetProcessById(pid); if (!old.WaitForExit(60_000)) Log("The old app did not end within a minute; trying anyway"); }
        catch (ArgumentException) { }   // already gone
        bool tray = false;
        foreach (var p in Process.GetProcessesByName(Core.Tray.OverlaySignals.TrayProcess))
            using (p)
            {
                string? path = null; try { path = p.MainModule?.FileName; } catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                if (path is not null && !path.StartsWith(appDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                try { p.Kill(); p.WaitForExit(5000); tray = true; } catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { Log($"Tray not stopped: {e.Message}"); }
            }
        bool ok;
        try { ok = UpdateInstaller.Apply(staging, appDir, Path.Combine(paths.UpdateDir, "previous"), Log); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log($"Update failed: {e.Message}"); ok = false; }
        Log(ok ? "Done; starting the app" : "Not updated; starting the old app");
        releaseInstance();   // this process holds the single-instance mutex open; the app it starts must be able to take it
        try
        {
            var psi = new ProcessStartInfo(Path.Combine(appDir, UpdateInstaller.ExeName)) { UseShellExecute = true, WorkingDirectory = appDir };
            if (ok) psi.Arguments = UpdatedArgument;
            Process.Start(psi)?.Dispose();
            if (tray && File.Exists(Path.Combine(appDir, "MazestaTray.exe"))) Process.Start(new ProcessStartInfo(Path.Combine(appDir, "MazestaTray.exe")) { UseShellExecute = true, WorkingDirectory = appDir })?.Dispose();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { Log($"Could not start the app: {e.Message}"); }
    }
}
