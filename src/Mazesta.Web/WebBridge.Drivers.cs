using System.Globalization; using System.IO; using System.Net.Http;
using Mazesta.Core.Drivers; using Mazesta.Desktop.Localization; using Mazesta.Diagnostics; using Mazesta.Diagnostics.Drivers; using Mazesta.Hardware.Wmi;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    /// <summary>A graphics card and its driver as Windows reports it now (read fresh: after an install the inventory's copy is old).</summary>
    private sealed record DriverGpu(string Name, string Vendor, string? WindowsVersion, string? NvidiaVersion, DateTime? Date);

    /// <summary>The drivers page's work, kept so the assistant reads the same answer the page shows.</summary>
    private Func<CancellationToken, Task<object>>? _driversCheck;

    /// <summary>
    /// The drivers page: the graphics card's driver against its maker's newest (NVIDIA asked directly, both Game Ready and Studio, with the line
    /// that suits the installed programs; AMD and Intel by their own detection tools, which the app opens), the drivers Windows Update offers this
    /// computer (installed one by one through Windows' own Update Agent), and the devices Windows has no working driver for. Nothing is downloaded or
    /// installed except by a button and a confirmation. An install holds the <see cref="WorkloadGate"/>: no test measures a card while its driver
    /// changes, and the assistant's model leaves the card first.
    /// </summary>
    private void RegisterDrivers()
    {
        var gate = _sp.GetRequiredService<WorkloadGate>(); var nvidia = new NvidiaDriverService(AiHttp, Path.Combine(_paths.DataRoot, "drivers"));
        List<DriverGpu> gpus = []; NvidiaProduct? product = null; IReadOnlyList<NvidiaRelease> gameReady = [], studio = [];
        string? nvError = null, checkedAt = null; bool checking = false; DriverAdviceResult? advice = null;
        IReadOnlyList<object> problems = [];
        // The NVIDIA install: idle, downloading, verifying, installing, done, failed.
        string nvState = "idle"; double nvProgress = 0; string? nvJobError = null, nvJobVersion = null; int? nvExit = null;
        // Windows Update: idle, searching, ready, installing, done, failed.
        string wuState = "idle"; string? wuError = null, wuSearchedAt = null; IReadOnlyList<DriverUpdate> wuList = []; IReadOnlyList<DriverInstallResult> wuResults = [];
        (int Index, int Count, string Title)? wuStep = null;
        CancellationTokenSource? jobCts = null;

        static object? Release(NvidiaRelease? r) => r is null ? null : new { version = r.Version, date = r.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), size = r.Size, name = r.Name, details = r.DetailsUrl };
        string? InstalledLine(string? v) => v is null ? null
            : gameReady.Any(r => r.Version == v) && !studio.Any(r => r.Version == v) ? "GameReady" : studio.Any(r => r.Version == v) && !gameReady.Any(r => r.Version == v) ? "Studio" : null;

        object State() => new
        {
            checking, checkedAt,
            gpus = gpus.Select(g => new
            {
                name = g.Name, vendor = g.Vendor, version = g.NvidiaVersion ?? g.WindowsVersion, date = g.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ageDays = g.Date is { } d ? (int)(DateTime.Now - d).TotalDays : (int?)null,
            }),
            nvidia = product is null && nvError is null ? null : new
            {
                error = nvError, product = product?.Name, geforce = product?.GeForce ?? false,
                gameReady = Release(gameReady.FirstOrDefault()), studio = Release(studio.FirstOrDefault()),
                installedLine = InstalledLine(gpus.FirstOrDefault(g => g.Vendor == "nvidia")?.NvidiaVersion),
                newerGameReady = gpus.FirstOrDefault(g => g.Vendor == "nvidia")?.NvidiaVersion is { } v1 && gameReady.FirstOrDefault() is { } g1 ? NvidiaDrivers.Compare(g1.Version, v1) > 0 : (bool?)null,
                newerStudio = gpus.FirstOrDefault(g => g.Vendor == "nvidia")?.NvidiaVersion is { } v2 && studio.FirstOrDefault() is { } s1 ? NvidiaDrivers.Compare(s1.Version, v2) > 0 : (bool?)null,
            },
            advice = advice is null ? null : new { suggested = advice.Suggested?.ToString(), creative = advice.Creative, games = advice.Games },
            job = new { state = nvState, progress = nvProgress, error = nvJobError, version = nvJobVersion, exitCode = nvExit },
            wu = new
            {
                state = wuState, error = wuError, searchedAt = wuSearchedAt, step = wuStep is { } st ? new { index = st.Index, count = st.Count, title = st.Title } : null,
                updates = wuList.Select(u => new { id = u.Id, title = u.Title, cls = u.DriverClass, model = u.Model, provider = u.Provider, date = u.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), mb = u.Bytes is { } b ? Math.Round(b / 1048576.0, 1) : (double?)null, licence = u.NeedsLicense }),
                results = wuResults.Select(r => new { id = r.Id, title = r.Title, ok = r.Succeeded, reboot = r.RebootRequired, error = r.Error }),
            },
            problems,
            busy = gate.Holder?.ToString(),
        };
        void Push() => PushSoon("drivers", State);

        static string VendorOf(string name) => name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? "nvidia" : name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? "amd"
            : name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "intel" : "other";

        async Task Check(CancellationToken ct)
        {
            checking = true; Push();
            try
            {
                var (cards, bad) = await Task.Run(() =>
                {
                    var q = new WmiQuery();
                    var cards = q.Query(@"root\cimv2", "SELECT Name,DriverVersion,DriverDate FROM Win32_VideoController")
                        .Select(r => (Name: (r["Name"] as string)?.Trim() ?? "", Version: r["DriverVersion"] as string, Date: r["DriverDate"] as string))
                        .Where(c => c.Name.Length > 0 && !c.Name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) && !c.Name.Contains("Remote", StringComparison.OrdinalIgnoreCase))
                        .Select(c => new DriverGpu(c.Name, VendorOf(c.Name), c.Version, VendorOf(c.Name) == "nvidia" ? NvidiaDrivers.FromWindowsVersion(c.Version) : null,
                            c.Date is { Length: >= 8 } d && DateTime.TryParseExact(d[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : null)).ToList();
                    // Code 45 is a device that is not plugged in now: not a driver problem.
                    var bad = q.Query(@"root\cimv2", "SELECT Name,PNPClass,ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0 AND ConfigManagerErrorCode <> 45")
                        .Select(r => (object)new { name = (r["Name"] as string)?.Trim() ?? Loc.Get("Drivers_UnknownDevice"), cls = r["PNPClass"] as string, code = Convert.ToInt32(r["ConfigManagerErrorCode"], CultureInfo.InvariantCulture) }).ToList();
                    return (cards, bad);
                }, ct).ConfigureAwait(true);
                gpus = cards; problems = bad;
                advice = DriverAdvice.Advise(await Task.Run(InstalledPrograms.Read, ct).ConfigureAwait(true));
                nvError = null; product = null; gameReady = []; studio = [];
                if (gpus.FirstOrDefault(g => g.Vendor == "nvidia") is { } nv)
                {
                    using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(TimeSpan.FromSeconds(40));
                    try
                    {
                        product = await nvidia.FindAsync(nv.Name, limit.Token).ConfigureAwait(true);
                        if (product is null) nvError = Loc.Format("Drivers_Nv_NotListed", nv.Name);
                        else
                        {
                            gameReady = await nvidia.ReleasesAsync(product, false, limit.Token).ConfigureAwait(true);
                            if (product.GeForce) studio = await nvidia.ReleasesAsync(product, true, limit.Token).ConfigureAwait(true);
                        }
                    }
                    catch (Exception e) when (e is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or System.Xml.XmlException)
                    {
                        if (ct.IsCancellationRequested) throw;
                        nvError = Loc.Format("Drivers_Nv_Offline", e.Message); _log.LogWarning(e, "NVIDIA driver lookup failed");
                    }
                }
                checkedAt = DateTime.Now.ToString("yyyy/MM/dd HH:mm", Loc.Culture);
            }
            finally { checking = false; Push(); }
        }

        _driversCheck = async ct =>
        {
            if (checkedAt is null && !checking) await _window.Dispatcher.InvokeAsync(() => Check(ct)).Task.Unwrap().ConfigureAwait(false);
            return State();
        };

        async Task InstallNvidia(string line, bool clean)
        {
            var list = line == "Studio" ? studio : gameReady;
            if (list.FirstOrDefault() is not { } release) { nvState = "failed"; nvJobError = Loc.Get("Drivers_Nv_NoRelease"); Push(); return; }
            using var lease = gate.TryEnter(Workload.Drivers);
            if (lease is null) { nvState = "failed"; nvJobError = Loc.Get($"Workload_Busy_{gate.Holder}"); Push(); return; }
            jobCts = new CancellationTokenSource(); nvJobError = null; nvExit = null; nvJobVersion = release.Version; nvProgress = 0;
            try
            {
                nvState = "downloading"; Push();
                string file = await nvidia.DownloadAsync(release, new Progress<double>(p => { nvProgress = p; Push(); }), jobCts.Token).ConfigureAwait(true);
                nvState = "verifying"; Push();
                if (await Task.Run(() => NvidiaDriverService.CheckSignature(file)).ConfigureAwait(true) is { } why) { File.Delete(file); throw new InvalidOperationException(Loc.Format("Drivers_Nv_BadSignature", why)); }
                nvState = "installing"; Push();
                _log.LogInformation("Installing NVIDIA driver {Version} ({Line}, clean: {Clean})", release.Version, line, clean);
                nvExit = await NvidiaDriverService.InstallAsync(file, clean, CancellationToken.None).ConfigureAwait(true);
                nvState = nvExit == 0 ? "done" : "failed";
                if (nvExit == 0) try { File.Delete(file); } catch (IOException) { }
                else nvJobError = Loc.Format("Drivers_Nv_ExitCode", nvExit);
                _log.LogInformation("NVIDIA installer ended with {Code}", nvExit);
            }
            catch (OperationCanceledException) { nvState = "idle"; }
            catch (Exception e) when (e is HttpRequestException or IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                nvState = "failed"; nvJobError = e.Message; _log.LogWarning(e, "NVIDIA driver install failed");
            }
            finally { jobCts.Dispose(); jobCts = null; Push(); }
            if (nvState == "done") await Check(CancellationToken.None).ConfigureAwait(true);
        }

        async Task SearchWindowsUpdate()
        {
            wuState = "searching"; wuError = null; wuResults = []; Push();
            try { wuList = await WindowsDriverUpdates.SearchAsync(CancellationToken.None).ConfigureAwait(true); wuState = "ready"; wuSearchedAt = DateTime.Now.ToString("yyyy/MM/dd HH:mm", Loc.Culture); }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                wuState = "failed"; wuError = Loc.Format("Drivers_Wu_Failed", $"0x{e.HResult:X8}", e.Message); _log.LogWarning(e, "Windows Update driver search failed");
            }
            finally { Push(); }
        }

        async Task InstallWindowsUpdate(IReadOnlyCollection<string> ids)
        {
            using var lease = gate.TryEnter(Workload.Drivers);
            if (lease is null) { wuState = "failed"; wuError = Loc.Get($"Workload_Busy_{gate.Holder}"); Push(); return; }
            jobCts = new CancellationTokenSource(); wuState = "installing"; wuError = null; Push();
            try
            {
                wuResults = await WindowsDriverUpdates.InstallAsync(ids, new Progress<(int, int, string)>(s => { wuStep = s; Push(); }), jobCts.Token).ConfigureAwait(true);
                wuState = "done";
            }
            catch (OperationCanceledException) { wuState = "done"; }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                wuState = "failed"; wuError = Loc.Format("Drivers_Wu_Failed", $"0x{e.HResult:X8}", e.Message); _log.LogWarning(e, "Windows Update driver install failed");
            }
            finally { wuStep = null; jobCts.Dispose(); jobCts = null; Push(); }
            // What was installed leaves the list; the problems and the card's version are read again.
            var done = wuResults.Where(r => r.Succeeded).Select(r => r.Id).ToHashSet();
            wuList = [.. wuList.Where(u => !done.Contains(u.Id))];
            await Check(CancellationToken.None).ConfigureAwait(true);
        }

        void OnGate(Workload? _) => Push();
        gate.Changed += OnGate; _cleanup.Add(() => { gate.Changed -= OnGate; jobCts?.Cancel(); });

        Method("drivers.state", _ => State());
        MethodAsync("drivers.check", async _ => { if (!checking) await Check(CancellationToken.None); return State(); });
        MethodAsync("drivers.wuSearch", async _ => { if (wuState is not ("searching" or "installing")) await SearchWindowsUpdate(); return State(); });
        Method("drivers.wuInstall", p =>
        {
            var ids = p.TryGetProperty("ids", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.Array ? a.EnumerateArray().Select(x => x.GetString() ?? "").Where(wuList.Select(u => u.Id).Contains).ToHashSet() : [];
            if (ids.Count == 0) throw new ArgumentException("nothing chosen");
            if (jobCts is not null) throw new InvalidOperationException(Loc.Get("Drivers_Busy"));
            if (gate.Holder is { } h) throw new InvalidOperationException(Loc.Get($"Workload_Busy_{h}"));
            _ = InstallWindowsUpdate(ids); return State();
        });
        Method("drivers.nvInstall", p =>
        {
            string line = Str(p, "line"); if (line is not ("GameReady" or "Studio")) throw new ArgumentException("line");
            if (jobCts is not null) throw new InvalidOperationException(Loc.Get("Drivers_Busy"));
            if (gate.Holder is { } h) throw new InvalidOperationException(Loc.Get($"Workload_Busy_{h}"));
            _ = InstallNvidia(line, Bool(p, "clean")); return State();
        });
        // Only the download can be stopped: an installer half way would leave the card without a working driver.
        Method("drivers.cancel", _ => { if (nvState == "downloading") jobCts?.Cancel(); return State(); });
        Method("drivers.open", p =>
        {
            string? target = Str(p, "what") switch
            {
                "amd" => "https://www.amd.com/en/support/download/drivers.html", "intel" => "https://www.intel.com/content/www/us/en/support/detect.html",
                "nvidia" => "https://www.nvidia.com/en-us/drivers/", "devmgr" => "devmgmt.msc", "wu" => "ms-settings:windowsupdate-optionalupdates", _ => null,
            };
            if (target is null) throw new ArgumentException("unknown");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
            return null;
        });
    }
}
