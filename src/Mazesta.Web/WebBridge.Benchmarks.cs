using System.IO; using System.Reflection; using System.Security.Cryptography; using System.Text;
using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using Mazesta.Desktop.Composition; using Mazesta.Desktop.Localization; using Mazesta.Desktop.ViewModels;
using Mazesta.Diagnostics.Benchmarks; using Mazesta.Diagnostics.Cpu; using Mazesta.Diagnostics.Gpu; using Mazesta.Diagnostics.Storage; using Mazesta.Hardware.Wmi; using Mazesta.Monitoring;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    private PeerDatabase? _peerDb;
    /// <summary>The comparison lists downloaded from the shop's site (Data/benchdb); the update page reloads them after a download.</summary>
    private PeerDatabase PeerDb => _peerDb ??= new PeerDatabase(_paths.BenchDbDir);
    private event Action? PeersChanged;
    /// <summary>The technician says this machine is overclocked: the runs made from now on are logged so (and ranked apart). Not kept across
    /// restarts, so the next customer's machine is not labelled by mistake.</summary>
    private static bool s_overclocked;

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
            PeerPart.Memory => BenchmarkDetails.MemorySummary(await inventory.GetAsync().ConfigureAwait(true)) is { } ram ? $"{ram} · {BenchmarkPeers.PartName(s.Cpu)}" : null,
            _ => null,
        };
        // The measured part's specifications and the rest of the machine, as read now (the inventory is read once and kept).
        async Task<IReadOnlyList<SpecItem>> DetailsOf(PeerPart part, string partName, IReadOnlyDictionary<string, string> options)
        {
            var inv = await inventory.GetAsync().ConfigureAwait(true);
            bool Same(string? a, string b) => string.Equals(BenchmarkPeers.PartName(a), BenchmarkPeers.PartName(b), StringComparison.OrdinalIgnoreCase);
            IEnumerable<SpecItem> own = part switch
            {
                PeerPart.Cpu => BenchmarkDetails.Cpu(inv.Cpu, CpuTopology.Cores),
                PeerPart.Gpu => BenchmarkDetails.Gpu(inv.Gpus.FirstOrDefault(g => Same(g.Name, partName)), VramBytes(engine, partName)),
                PeerPart.Memory => BenchmarkDetails.Memory(inv),
                PeerPart.Drive => BenchmarkDetails.Drive(inv.Storage.FirstOrDefault(d => Same(d.FriendlyName, partName))),
                _ => [],
            };
            return [.. own, .. BenchmarkDetails.Machine(inv, part)];
        }

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
                var details = await DetailsOf(h.Part, part, options);
                runs.Append(new BenchmarkRun(Guid.NewGuid().ToString("N"), c.Current.At, run.Definition.Id.Value, h.Version, BenchmarkPeers.Settings(options), BenchmarkPeers.PartName(part),
                    s.Hash, Environment.MachineName, s.Name, c.Current.Value, c.Current.Unit, app, c.Current.Metrics, s_overclocked, details));
                memo.Clear(); PushSoon("bench", State);
            }
            catch (Exception e) { _log.LogWarning(e, "Benchmark run not logged for comparison"); }
        }
        void OnFinished(RecordedBenchmark run) => _window.Dispatcher.BeginInvoke(() => OnFinishedUi(run));
        runner.Finished += OnFinished; _cleanup.Add(() => runner.Finished -= OnFinished);
        void OnPeers() { memo.Clear(); PushSoon("bench", State); }
        PeersChanged += OnPeers; _cleanup.Add(() => PeersChanged -= OnPeers);

        // This system's latest logged run of a list: its conditions and specifications stand for "this system" in a comparison.
        BenchmarkRun? MyLast(string table) => System()?.Hash is { } hash ? runs.Of(table).FirstOrDefault(x => x.System == hash) : null;
        object? Best(BenchmarkRowViewModel r) => System() is { } s && records.Best(s.Key, Key(r)) is { } b ? Record(b) : null;
        object? Compared(BenchmarkRowViewModel r) => compared.GetValueOrDefault(r.Benchmark.Definition.Id.Value) is { } c
            ? new { now = Record(c.Current), previous = c.Previous is { } p ? Record(p) : null, change = c.ChangePercent, saved = c.Saved } : null;

        // This system's result for the comparison: the run just made, or else the best kept one; and the part it measured, as the run log last named it
        // on this machine (a drive or memory name needs a WMI read, which a state push must not wait for).
        (double? Value, string? Part) Mine(BenchmarkRowViewModel r, string table)
        {
            double? value = compared.GetValueOrDefault(r.Benchmark.Definition.Id.Value)?.Current.Value ?? (System() is { } s ? records.Best(s.Key, Key(r))?.Value : null);
            string? part = MyLast(table)?.Part;
            if (part is null && Headline(r) is { Part: PeerPart.Cpu } && System() is { } sys) part = sys.Cpu;
            if (part is null && Headline(r) is { Part: PeerPart.Gpu }) part = GpuName(r.OptionValues());
            return (value, part);
        }
        PeerRanking Ranking(BenchmarkRowViewModel r, HeadlineMetric h, string table, double? mine, string? part)
            => BenchmarkPeers.Rank(PeerDb.Table(table), runs.Entries(table), mine ?? double.NaN, part, h.HigherIsBetter);
        static object? GapJson(PeerGap? g) => g is { } x ? new { text = x.Text, lead = x.TheyLead, equal = x.Equal } : null;
        object PeerJson(PeerRow p, string unit) => new
        {
            part = p.Entry.Part, oc = p.Entry.Overclocked, value = Units.FormatMeasured(p.Entry.Median, unit), best = Units.FormatMeasured(p.Entry.Best, unit),
            systems = p.Entry.Systems, runs = p.Entry.Runs, diff = double.IsFinite(p.DiffPercent) ? p.DiffPercent : (double?)null, gap = GapJson(p.Gap), local = p.Local, same = p.Same,
        };
        string UnitOf(string table) => PeerDb.Table(table)?.Unit ?? runs.Of(table).FirstOrDefault()?.Unit ?? "";
        // The shop's reference runs of a list: the published ones, and ones marked on this copy and not yet published.
        IReadOnlyList<(FeaturedRun Run, bool Local)> Featured(string table)
        {
            var published = PeerDb.Table(table)?.Featured ?? [];
            var ids = published.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
            return [.. published.Select(f => (f, false)), .. (runs.Table(table)?.Featured ?? []).Where(f => !ids.Contains(f.Id)).Select(f => (f, true))];
        }
        object FeaturedJson(FeaturedRun f, bool local, double? mine, HeadlineMetric h, string unit) => new
        {
            id = f.Id, part = f.Part, oc = f.Overclocked, note = f.Note, local, value = Units.FormatMeasured(f.Value, unit), at = f.At.ToLocalTime().ToString("yyyy/MM/dd", Loc.Culture),
            gap = GapJson(mine is { } m ? BenchmarkPeers.Gap(m, f.Value, h.HigherIsBetter) : null),
        };

        // The row's standing, kept small for the frequent state pushes: the counts, the few entries around this result and the featured runs. The
        // whole list, and any entry's details, are asked for apart.
        object? Peers(BenchmarkRowViewModel r)
        {
            if (Headline(r) is not { } h) return null;
            string table = Table(r, h); var (mine, part) = Mine(r, table);
            string key = $"{table}#{mine}#{part}";
            if (memo.TryGetValue(key, out var cached)) return cached;
            var k = Ranking(r, h, table, mine, part); string unit = UnitOf(table);
            int at = mine is null ? 0 : k.MineIndex, from = Math.Max(0, at - 3), to = Math.Min(k.Rows.Count, at + 3);
            var featured = Featured(table);
            return memo[key] = new
            {
                total = k.Total, beaten = mine is null ? (int?)null : k.Beaten, mineIndex = mine is null ? (int?)null : k.MineIndex, from,
                around = k.Rows.Skip(from).Take(to - from).Select(p => PeerJson(p, unit)),
                featured = featured.Take(5).Select(f => FeaturedJson(f.Run, f.Local, mine, h, unit)), featuredTotal = featured.Count,
                mine = mine is { } m ? Units.FormatMeasured(m, unit) : null, part, oc = MyLast(table)?.Overclocked ?? false,
            };
        }
        object State() => new
        {
            running = bench.IsRunning, queue = bench.QueueText, canRunSelected = bench.RunSelectedCommand.CanExecute(null), overclocked = s_overclocked,
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
        // The whole comparison list of a row (it can hold thousands of models; the page shows it a page at a time and searches it). Details of an
        // entry are not in it: bench.detail brings one entry's when it is opened.
        Method("bench.peers", p =>
        {
            var r = Row(p);
            if (Headline(r) is not { } h) return null;
            string table = Table(r, h); var (mine, part) = Mine(r, table); var k = Ranking(r, h, table, mine, part); string unit = UnitOf(table);
            return new
            {
                name = r.Name, metric = Loc.Get(h.Key), higherIsBetter = h.HigherIsBetter, mine = mine is { } m ? Units.FormatMeasured(m, unit) : null, part, oc = MyLast(table)?.Overclocked ?? false,
                mineIndex = mine is null ? (int?)null : k.MineIndex, beaten = k.Beaten, built = PeerDb.Table(table)?.Built.ToLocalTime().ToString("yyyy/MM/dd", Loc.Culture),
                rows = k.Rows.Select(x => PeerJson(x, unit)), featured = Featured(table).Select(f => FeaturedJson(f.Run, f.Local, mine, h, unit)),
            };
        });
        // One entry, or one featured run, beside this system: what was measured during each run and the specifications of each part and machine.
        Method("bench.detail", p =>
        {
            var r = Row(p);
            if (Headline(r) is not { } h) return null;
            string table = Table(r, h); string unit = UnitOf(table); var last = MyLast(table);
            var (mine, part) = Mine(r, table);
            object? theirs = null;
            if (Str(p, "run") is { Length: > 0 } id)
            {
                if (Featured(table).FirstOrDefault(f => f.Run.Id == id).Run is { } f) theirs = Detail(f.Value, unit, f.Overclocked, f.At, f.Metrics, f.Details);
            }
            else
            {
                string name = Str(p, "part"); bool oc = Bool(p, "oc");
                var row = Ranking(r, h, table, mine, part).Rows.FirstOrDefault(x => x.Entry.Overclocked == oc && string.Equals(x.Entry.Part, name, StringComparison.OrdinalIgnoreCase));
                if (row?.Entry.Sample is { } s) theirs = Detail(s.Value, unit, s.Overclocked, s.At, s.Metrics, s.Details);
            }
            var metrics = last?.Metrics ?? compared.GetValueOrDefault(r.Benchmark.Definition.Id.Value)?.Current.Metrics ?? (System() is { } sys ? records.Best(sys.Key, Key(r))?.Metrics : null);
            return new { mine = mine is { } v ? Detail(v, unit, last?.Overclocked ?? false, last?.At, metrics, last?.Details) : null, theirs };
        });
        // This copy's own runs of a row's list: every machine it has measured, newest first, each with its conditions, specifications and the shop's marks.
        Method("bench.history", p =>
        {
            var r = Row(p);
            if (Headline(r) is not { } h) return Array.Empty<object>();
            var marks = runs.Marks.All();
            return runs.Of(Table(r, h)).Take(500).Select(x => new
            {
                id = x.Id, at = x.At.ToLocalTime().ToString("yyyy/MM/dd HH:mm", Loc.Culture), machine = x.Machine, part = x.Part, value = Units.FormatMeasured(x.Value, x.Unit), app = x.App,
                featured = marks.GetValueOrDefault(x.Id)?.Featured ?? false, note = marks.GetValueOrDefault(x.Id)?.Note, detail = Detail(x.Value, x.Unit, x.Overclocked, x.At, x.Metrics, x.Details),
            });
        });
        // The shop's word on a logged run: featured (a reference result on every copy once published) and overclocked.
        Method("bench.mark", p =>
        {
            string id = Str(p, "run");
            var run = runs.Find(id) ?? throw new ArgumentException("unknown run");
            var had = runs.Marks.All().GetValueOrDefault(id);
            bool featured = p.TryGetProperty("featured", out _) ? Bool(p, "featured") : had?.Featured ?? false;
            bool? oc = p.TryGetProperty("oc", out _) ? Bool(p, "oc") : had?.Overclocked;
            string? note = p.TryGetProperty("note", out _) ? Str(p, "note").Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 120)] : null : had?.Note;
            runs.Marks.Set(id, new BenchmarkMark(featured, oc, note, DateTimeOffset.UtcNow));
            memo.Clear(); PushSoon("bench", State);
            return null;
        });
        Method("bench.oc", p => { s_overclocked = Bool(p, "value"); PushSoon("bench", State); return null; });
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
        at = r.At.ToLocalTime().ToString("yyyy/MM/dd HH:mm", Loc.Culture), metrics = Metrics(r.Metrics, r.Key),
    };

    /// <summary>A run's numbers for the page, in two groups: what the work achieved, and the conditions the monitor measured during it.</summary>
    private static object Metrics(IReadOnlyList<BenchmarkMetric>? metrics, string? headline = null) => new
    {
        results = (metrics ?? []).Where(m => !BenchmarkDetails.IsCondition(m.Key) && m.Key != headline).Select(MetricJson),
        conditions = (metrics ?? []).Where(m => BenchmarkDetails.IsCondition(m.Key)).Select(MetricJson),
    };
    private static object MetricJson(BenchmarkMetric m) => new { name = Loc.Get(m.Key), value = m.Unit.Length == 0 ? Units.FormatMeasured(m.Value, "", 0) : Units.FormatMeasured(m.Value, m.Unit) };

    /// <summary>One run in full: its number, whether it was overclocked, when, its measured numbers and its part's and machine's specifications.</summary>
    private static object Detail(double value, string unit, bool overclocked, DateTimeOffset? at, IReadOnlyList<BenchmarkMetric>? metrics, IReadOnlyList<SpecItem>? details) => new
    {
        value = Units.FormatMeasured(value, unit), oc = overclocked, at = at?.ToLocalTime().ToString("yyyy/MM/dd", Loc.Culture), metrics = Metrics(metrics),
        part = (details ?? []).Where(d => d.Group == BenchmarkDetails.PartGroup).Select(SpecJson),
        system = (details ?? []).Where(d => d.Group == BenchmarkDetails.SystemGroup).Select(SpecJson),
    };
    private static object SpecJson(SpecItem s) => new { name = Loc.Get(s.Key), value = s.Value };

    /// <summary>The GPU a run chose (the option holds "name|LUID"), or the one it runs on by default.</summary>
    private static string? GpuName(IReadOnlyDictionary<string, string> options)
    {
        string key = options.GetValueOrDefault(GpuDevices.OptionKey) is { Length: > 0 } chosen ? chosen : GpuDevices.Choices().FirstOrDefault()?.Value ?? "";
        int bar = key.LastIndexOf('|');
        return (bar > 0 ? key[..bar] : key) is { Length: > 0 } name ? name : null;
    }

    /// <summary>A GPU's own memory size as the monitor reads it (WMI's figure stops at 4 GB), or null.</summary>
    private static double? VramBytes(PollingEngine engine, string gpu)
    {
        var gpus = engine.Hardware.Where(n => n.Kind == HardwareKind.Gpu && n.ParentId is null).ToList();
        var node = gpus.Count == 1 ? gpus[0] : gpus.FirstOrDefault(n => string.Equals(BenchmarkPeers.PartName(n.Name), BenchmarkPeers.PartName(gpu), StringComparison.OrdinalIgnoreCase));
        var sensor = node?.Sensors.FirstOrDefault(s => s.Role == SensorRole.GpuVramTotal);
        if (sensor is null) return null;
        var raw = engine.History.GetRaw(sensor.Id);
        for (int i = raw.Values.Length - 1; i >= 0; i--)
            if (!float.IsNaN(raw.Values[i]) && raw.Values[i] > 0)
                return sensor.Unit switch { Unit.Megabyte => raw.Values[i] * 1048576.0, Unit.Gigabyte => raw.Values[i] * 1073741824.0, _ => null };
        return null;
    }
}
