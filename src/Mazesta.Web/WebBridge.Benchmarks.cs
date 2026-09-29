using System.Globalization; using System.IO; using System.Reflection; using System.Security.Cryptography; using System.Text;
using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using Mazesta.Desktop.Composition; using Mazesta.Desktop.Localization; using Mazesta.Desktop.ViewModels;
using Mazesta.Diagnostics.Benchmarks; using Mazesta.Diagnostics.Gpu; using Mazesta.Diagnostics.Storage; using Mazesta.Hardware.Wmi; using Mazesta.Monitoring;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    private PeerDatabase? _peerDb;
    /// <summary>The comparison lists downloaded from the shop's site (Data/benchdb); the update page reloads them after a download.</summary>
    private PeerDatabase PeerDb => _peerDb ??= new PeerDatabase(_paths.BenchDbDir);
    private event Action? PeersChanged;

    /// <summary>This machine as the benchmark records know it: <see cref="Key"/> its name with its CPU and GPUs (a portable copy carried from machine to
    /// machine keeps each one's records apart, and a changed CPU or GPU starts fresh ones), and a hash of it that names it in the comparison lists.</summary>
    private sealed record SystemId(string Key, string Name, string Cpu, string Hash);

    private void RegisterBenchmarks()
    {
        var bench = _sp.GetRequiredService<Func<BenchmarksViewModel>>()();
        _cleanup.Add(bench.Dispose);
        // The best result of each benchmark on this machine, kept across runs and restarts; a run is compared with it when it finishes. Every ranked
        // run also goes to the run log, whatever machine this copy is on: the shop builds the published comparison lists from those logs.
        var records = new BenchmarkRecords(_paths.DataRoot); var runs = new BenchmarkRunLog(_paths.DataRoot);
        var runner = _sp.GetRequiredService<BenchmarkRunner>(); var engine = _sp.GetRequiredService<PollingEngine>();
        var inventory = _sp.GetRequiredService<InventoryCache>(); var wmi = _sp.GetRequiredService<IWmiQuery>();
        string app = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
        var compared = new Dictionary<string, BenchmarkComparison?>(); var memo = new Dictionary<string, object?>();

        // The hardware list arrives a few seconds after start-up; until then the system is not known (0.6 and earlier saved records under the bare
        // machine name at that moment; they are moved under the full name here).
        SystemId? known = null;
        SystemId? System()
        {
            if (known is not null) return known;
            string? cpu = engine.Hardware.FirstOrDefault(n => n.Kind == HardwareKind.Cpu && n.ParentId is null)?.Name;
            if (cpu is null) return null;
            var gpus = engine.Hardware.Where(n => n.Kind == HardwareKind.Gpu && n.ParentId is null).Select(n => n.Name).Order(StringComparer.Ordinal);
            string key = string.Join(" | ", [Environment.MachineName, cpu, .. gpus]);
            known = new SystemId(key, $"{Environment.MachineName} · {cpu}", cpu, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16]);
            records.AdoptUnnamed(key, Environment.MachineName);
            return known;
        }
        string Key(BenchmarkRowViewModel r) => BenchmarkRecords.RecordKey(r.Benchmark.Definition.Id.Value, r.OptionValues());
        HeadlineMetric? Headline(BenchmarkRowViewModel r) => BenchmarkRecords.Headline(r.Benchmark.Definition.Id.Value) is { Part: not PeerPart.None } h ? h : null;
        string Table(BenchmarkRowViewModel r, HeadlineMetric h) => BenchmarkPeers.TableKey(r.Benchmark.Definition.Id.Value, h.Version, BenchmarkPeers.Settings(r.OptionValues()));

        async Task<string?> PartOf(PeerPart part, IReadOnlyDictionary<string, string> options, SystemId s) => part switch
        {
            PeerPart.Cpu => s.Cpu,
            PeerPart.Gpu => GpuName(options),
            PeerPart.Drive => StorageExecutor.TargetOf(options.GetValueOrDefault(StorageExecutor.DriveOption) ?? "") is { } target ? await Task.Run(() => DriveModels.Of(wmi, target)).ConfigureAwait(true) : null,
            PeerPart.Memory => MemorySummary(await inventory.GetAsync().ConfigureAwait(true)) is { } ram ? $"{ram} · {BenchmarkPeers.PartName(s.Cpu)}" : null,
            _ => null,
        };

        async void OnFinishedUi(RecordedBenchmark run)
        {
            // The options are read from the row as it is now: they cannot be changed while its run is going.
            var row = bench.Rows.FirstOrDefault(r => r.Benchmark.Definition.Id == run.Definition.Id);
            if (row is null) return;
            var s = System() ?? new SystemId(Environment.MachineName + " | ", Environment.MachineName, "", "");
            var c = records.Offer(s.Key, s.Name, Key(row), run.Result);
            compared[run.Definition.Id.Value] = c;
            if (c is { Saved: false }) _log.LogInformation("Benchmark {Id}: {Value} is below the record {Best}; not kept", run.Definition.Id.Value, c.Current.Value, c.Previous?.Value);
            PushSoon("bench", State);
            if (c is null || Headline(row) is not { } h || s.Hash.Length == 0) return;
            try
            {
                var options = row.OptionValues();
                string? part = await PartOf(h.Part, options, s);
                if (string.IsNullOrWhiteSpace(part)) { _log.LogInformation("Benchmark {Id}: the measured part is not known; the run is not added to the comparison log", run.Definition.Id.Value); return; }
                runs.Append(new BenchmarkRun(Guid.NewGuid().ToString("N"), c.Current.At, run.Definition.Id.Value, h.Version, BenchmarkPeers.Settings(options), BenchmarkPeers.PartName(part),
                    s.Hash, Environment.MachineName, s.Name, c.Current.Value, c.Current.Unit, app, c.Current.Metrics));
                memo.Clear(); PushSoon("bench", State);
            }
            catch (Exception e) { _log.LogWarning(e, "Benchmark run not logged for comparison"); }
        }
        void OnFinished(RecordedBenchmark run) => _window.Dispatcher.BeginInvoke(() => OnFinishedUi(run));
        runner.Finished += OnFinished; _cleanup.Add(() => runner.Finished -= OnFinished);
        void OnPeers() { memo.Clear(); PushSoon("bench", State); }
        PeersChanged += OnPeers; _cleanup.Add(() => PeersChanged -= OnPeers);

        object? Best(BenchmarkRowViewModel r) => System() is { } s && records.Best(s.Key, Key(r)) is { } b ? Record(b) : null;
        object? Compared(BenchmarkRowViewModel r) => compared.GetValueOrDefault(r.Benchmark.Definition.Id.Value) is { } c
            ? new { now = Record(c.Current), previous = c.Previous is { } p ? Record(p) : null, change = c.ChangePercent, saved = c.Saved } : null;

        // This system's result for the comparison: the run just made, or else the best kept one; and the part it measured, as the run log last named it
        // on this machine (a drive or memory name needs a WMI read, which a state push must not wait for).
        (double? Value, string? Part) Mine(BenchmarkRowViewModel r, string table)
        {
            double? value = compared.GetValueOrDefault(r.Benchmark.Definition.Id.Value)?.Current.Value ?? (System() is { } s ? records.Best(s.Key, Key(r))?.Value : null);
            string? hash = System()?.Hash;
            string? part = runs.Of(table).FirstOrDefault(x => x.System == hash)?.Part;
            if (part is null && Headline(r) is { Part: PeerPart.Cpu } && System() is { } sys) part = sys.Cpu;
            if (part is null && Headline(r) is { Part: PeerPart.Gpu }) part = GpuName(r.OptionValues());
            return (value, part);
        }
        PeerRanking Ranking(BenchmarkRowViewModel r, HeadlineMetric h, string table, double? mine, string? part)
            => BenchmarkPeers.Rank(PeerDb.Table(table), runs.Entries(table), mine ?? double.NaN, part, h.HigherIsBetter);
        object PeerJson(PeerRow p, string unit) => new
        {
            part = p.Entry.Part, value = Units.FormatMeasured(p.Entry.Median, unit), best = Units.FormatMeasured(p.Entry.Best, unit),
            systems = p.Entry.Systems, runs = p.Entry.Runs, diff = double.IsFinite(p.DiffPercent) ? p.DiffPercent : (double?)null, local = p.Local, same = p.Same,
        };
        string UnitOf(string table, PeerRanking k) => PeerDb.Table(table)?.Unit ?? runs.Of(table).FirstOrDefault()?.Unit ?? "";

        // The row's standing, kept small for the frequent state pushes: the counts and the few entries around this result. The whole list is asked for apart.
        object? Peers(BenchmarkRowViewModel r)
        {
            if (Headline(r) is not { } h) return null;
            string table = Table(r, h); var (mine, part) = Mine(r, table);
            string key = $"{table}#{mine}#{part}";
            if (memo.TryGetValue(key, out var cached)) return cached;
            var k = Ranking(r, h, table, mine, part); string unit = UnitOf(table, k);
            int at = mine is null ? 0 : k.MineIndex, from = Math.Max(0, at - 3), to = Math.Min(k.Rows.Count, at + 3);
            return memo[key] = new
            {
                total = k.Total, beaten = mine is null ? (int?)null : k.Beaten, mineIndex = mine is null ? (int?)null : k.MineIndex, from,
                around = k.Rows.Skip(from).Take(to - from).Select(p => PeerJson(p, unit)),
                mine = mine is { } m ? Units.FormatMeasured(m, unit) : null, part,
            };
        }
        object State() => new
        {
            running = bench.IsRunning, queue = bench.QueueText, canRunSelected = bench.RunSelectedCommand.CanExecute(null),
            rows = bench.Rows.Select(r => new
            {
                id = r.Benchmark.Definition.Id.Value, name = r.Name, component = r.Benchmark.Component.ToString(), selected = r.IsSelected, duration = r.DurationText,
                percent = r.PercentComplete, status = r.StatusText, active = r.IsActive, detail = r.Detail, unavailable = r.UnavailableText,
                options = r.Options.Select(Option), metrics = r.Metrics.Select(m => new { name = m.Name, value = m.Value }),
                best = Best(r), compared = Compared(r), peers = r.IsActive ? null : Peers(r),
            }),
        };
        Mirror("bench", bench, State, [bench.Rows, .. bench.Rows.Select(r => r.Metrics)]);
        foreach (var o in bench.Rows.SelectMany(r => r.Options)) o.PropertyChanged += (_, _) => PushSoon("bench", State);
        // The hardware list is read a few seconds after start-up: the records under the full system name show once it is in.
        void OnSnapshot(SensorSnapshot _) { if (System() is null) return; engine.SnapshotPublished -= OnSnapshot; PushSoon("bench", State); }
        if (System() is null) { engine.SnapshotPublished += OnSnapshot; _cleanup.Add(() => engine.SnapshotPublished -= OnSnapshot); }

        BenchmarkRowViewModel Row(System.Text.Json.JsonElement p) => bench.Rows.FirstOrDefault(r => r.Benchmark.Definition.Id.Value == Str(p, "id")) ?? throw new ArgumentException("unknown benchmark");
        Method("bench.state", _ => State());
        // The whole comparison list of a row (it can hold thousands of models; the page shows it a page at a time and searches it).
        Method("bench.peers", p =>
        {
            var r = Row(p);
            if (Headline(r) is not { } h) return null;
            string table = Table(r, h); var (mine, part) = Mine(r, table); var k = Ranking(r, h, table, mine, part); string unit = UnitOf(table, k);
            return new
            {
                name = r.Name, metric = Loc.Get(h.Key), higherIsBetter = h.HigherIsBetter, mine = mine is { } m ? Units.FormatMeasured(m, unit) : null, part,
                mineIndex = mine is null ? (int?)null : k.MineIndex, beaten = k.Beaten, built = PeerDb.Table(table)?.Built.ToLocalTime().ToString("yyyy/MM/dd", Loc.Culture),
                rows = k.Rows.Select(x => PeerJson(x, unit)),
            };
        });
        // This copy's own runs of a row's list: every machine it has measured, newest first (what the shop gathers for the published lists).
        Method("bench.history", p =>
        {
            var r = Row(p);
            if (Headline(r) is not { } h) return Array.Empty<object>();
            return runs.Of(Table(r, h)).Take(500).Select(x => new
            {
                at = x.At.ToLocalTime().ToString("yyyy/MM/dd HH:mm", Loc.Culture), machine = x.Machine, part = x.Part, value = Units.FormatMeasured(x.Value, x.Unit), app = x.App,
            });
        });
        Method("bench.set", p =>
        {
            var row = Row(p);
            switch (Str(p, "field"))
            {
                case "selected": row.IsSelected = Bool(p, "value"); break;
                case "duration": row.DurationText = Str(p, "value"); break;
                case "option": SetOption(row.Options, Str(p, "key"), Str(p, "value")); break;
                default: throw new ArgumentException("unknown field");
            }
            return null;
        });
        MethodAsync("bench.exec", async p =>
        {
            switch (Str(p, "cmd"))
            {
                case "run": var row = Row(p); if (bench.RunCommand.CanExecute(row)) await bench.RunCommand.ExecuteAsync(row); break;
                case "runSelected": if (bench.RunSelectedCommand.CanExecute(null)) await bench.RunSelectedCommand.ExecuteAsync(null); break;
                case "cancel": if (bench.CancelCommand.CanExecute(null)) bench.CancelCommand.Execute(null); break;
                case "selectAll": bench.SelectAllCommand.Execute(null); break;
                case "clear": bench.ClearSelectionCommand.Execute(null); break;
                case "openRuns": Directory.CreateDirectory(runs.Folder); Open(runs.Folder); break;
                default: throw new ArgumentException("unknown command");
            }
            return null;
        });
    }

    private static object Record(BenchmarkRecord r) => new
    {
        name = Loc.Get(r.Key), value = Units.FormatMeasured(r.Value, r.Unit), raw = r.Value,
        at = r.At.ToLocalTime().ToString("yyyy/MM/dd HH:mm", Loc.Culture),
    };

    /// <summary>The GPU a run chose (the option holds "name|LUID"), or the one it runs on by default.</summary>
    private static string? GpuName(IReadOnlyDictionary<string, string> options)
    {
        string key = options.GetValueOrDefault(GpuDevices.OptionKey) is { Length: > 0 } chosen ? chosen : GpuDevices.Choices().FirstOrDefault()?.Value ?? "";
        int bar = key.LastIndexOf('|');
        return (bar > 0 ? key[..bar] : key) is { Length: > 0 } name ? name : null;
    }

    /// <summary>The installed memory as the modules report it: "32 GB (2×16 GB) 6000 MT/s". Null when the modules cannot be read.</summary>
    internal static string? MemorySummary(HardwareInventory inv)
    {
        var modules = inv.MemoryModules.Where(m => m.CapacityBytes > 0).ToList();
        if (modules.Count == 0) return null;
        long total = modules.Sum(m => m.CapacityBytes!.Value) >> 30;
        string sizes = string.Join(" + ", modules.GroupBy(m => m.CapacityBytes!.Value >> 30).OrderByDescending(g => g.Key).Select(g => $"{g.Count()}×{g.Key} GB"));
        int speed = modules.Select(m => m.ConfiguredSpeedMts ?? m.SpeedMts ?? 0).Where(v => v > 0).DefaultIfEmpty(0).Min();
        return string.Create(CultureInfo.InvariantCulture, $"{total} GB ({sizes})") + (speed > 0 ? string.Create(CultureInfo.InvariantCulture, $" {speed} MT/s") : "");
    }
}
