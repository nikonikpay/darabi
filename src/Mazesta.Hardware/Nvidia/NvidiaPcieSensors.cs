using Mazesta.Core.Hardware; using Mazesta.Core.Providers;
namespace Mazesta.Hardware.Nvidia;

/// <summary>
/// The sensor provider with each NVIDIA card's PCI Express error counters added to that card's sensors (LibreHardwareMonitor does not read them):
/// the counters the driver keeps (<see cref="NvmlPcieCounters"/>) and their total. A card is matched to its node by name, in order among cards of
/// the same name. Everything else passes through untouched.
/// </summary>
public sealed class NvidiaPcieSensors : ISensorProvider, IFanControlSource
{
    private sealed record Attached(PcieCard Card, SensorDefinition? Total, IReadOnlyList<SensorDefinition> Sensors);
    private readonly ISensorProvider inner;
    private readonly List<Attached> _cards = [];
    private bool _starting;
    public NvidiaPcieSensors(ISensorProvider inner) { this.inner = inner; inner.StatusChanged += s => { if (!_starting) StatusChanged?.Invoke(s); }; }
    public IReadOnlyList<FanChannel> Fans() => inner is IFanControlSource f ? f.Fans() : [];
    public bool SetManual(string id, double percent) => inner is IFanControlSource f && f.SetManual(id, percent);
    public bool SetAuto(string id) => inner is IFanControlSource f && f.SetAuto(id);
    public string Name => inner.Name;
    public ProviderStatus Status => inner.Status;
    // The inner provider says Ready inside its Start, before the cards' counters are added here; listeners read Hardware on it, so the
    // statuses of the start are held back and the last one told once Hardware is complete.
    public event Action<ProviderStatus>? StatusChanged;
    public IReadOnlyList<HardwareNode> Hardware { get; private set; } = [];

    public void Start()
    {
        _starting = true;
        try { inner.Start(); Attach(); }
        finally { _starting = false; }
        StatusChanged?.Invoke(inner.Status);
    }

    private void Attach()
    {
        var nodes = inner.Hardware.ToList();
        try
        {
            var gpus = nodes.Select((n, i) => (n, i)).Where(x => x.n.Kind == HardwareKind.Gpu && x.n.Vendor == HardwareVendor.Nvidia && x.n.ParentId is null).ToList();
            var taken = new HashSet<int>();
            foreach (var card in NvmlPcieCounters.Find())
            {
                var match = gpus.FirstOrDefault(g => !taken.Contains(g.i) && string.Equals(g.n.Name.Trim(), card.Name.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match.n is null) continue;
                taken.Add(match.i);
                var node = match.n; int ordinal = node.Sensors.Count == 0 ? 0 : node.Sensors.Max(s => s.Ordinal) + 1;
                SensorDefinition Def(string path, string name, SensorRole role) => new(SensorId.Create(node.Id, path), node.Id, name, SensorKind.Count, Unit.None, role, ordinal++);
                bool hasErrors = card.Counters.Any(c => c.Role == SensorRole.GpuPcieErrorCounter);
                var total = hasErrors ? Def("pcie/total", NvmlPcieCounters.TotalName, SensorRole.GpuPcieErrorTotal) : null;
                var sensors = card.Counters.Select(c => Def($"pcie/{c.Field}", c.Name, c.Role)).ToList();
                _cards.Add(new(card, total, sensors));
                nodes[match.i] = node with { Sensors = [.. node.Sensors, .. total is null ? [] : new[] { total }, .. sensors] };
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { _cards.Clear(); }
        Hardware = nodes;
    }

    public PollResult Poll(PollRequest request)
    {
        var result = inner.Poll(request);
        if (_cards.Count == 0) return result;
        var readings = result.Readings.ToList();
        foreach (var a in _cards)
        {
            var values = NvmlPcieCounters.Read(a.Card);
            for (int i = 0; i < values.Length; i++) readings.Add(Reading(a.Sensors[i], values[i], request.Now));
            if (a.Total is not null)
            {
                var errors = a.Card.Counters.Select((c, i) => (c, v: values[i])).Where(x => x.c.Role == SensorRole.GpuPcieErrorCounter).ToList();
                // A total is given only when every part of it was read this time, so it never drops for a counter that failed to answer.
                readings.Add(Reading(a.Total, errors.All(x => x.v is not null) ? errors.Sum(x => x.v!.Value) : null, request.Now));
            }
        }
        return result with { Readings = readings };
    }

    private SensorReading Reading(SensorDefinition s, double? v, DateTimeOffset now) => new(s.Id, v, now, ReadingValidator.Validate(s.Kind, v), "NVML");

    public void Dispose() => inner.Dispose();
}
