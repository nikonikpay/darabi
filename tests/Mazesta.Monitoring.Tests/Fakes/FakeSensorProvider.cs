using Mazesta.Core.Hardware; using Mazesta.Hardware;
namespace Mazesta.Monitoring.Tests.Fakes;
public sealed class FakeSensorProvider : ISensorProvider
{
    public string Name => "fake"; public ProviderStatus Status { get; set; } = ProviderStatus.NotStarted; public event Action<ProviderStatus>? StatusChanged;
    public List<HardwareNode> Nodes { get; } = []; public IReadOnlyList<HardwareNode> Hardware => Nodes;
    public List<PollRequest> Requests { get; } = []; public Func<PollRequest, PollResult>? OnPoll; public Action? OnPollSideEffect; public bool Started, Disposed;
    public void Start() { Started = true; Status = ProviderStatus.Ready(Nodes.Sum(n => n.Sensors.Count)); StatusChanged?.Invoke(Status); }
    public PollResult Poll(PollRequest r)
    {
        Requests.Add(r); OnPollSideEffect?.Invoke();
        if (OnPoll is not null) return OnPoll(r);
        var readings = Nodes.SelectMany(n => n.Sensors).Select(s => new SensorReading(s.Id, 42, r.Now, DataQuality.Ok, Name)).ToList();
        return new PollResult(readings, Nodes.ToDictionary(n => n.Id, n => NodeStatus.Healthy(r.Now)));
    }
    public void Dispose() => Disposed = true;
    public static HardwareNode Node(HardwareKind kind, string id, params string[] sensors)
    { var hid = new HardwareId(id); return new HardwareNode(hid, kind, HardwareVendor.Unknown, id, null, true, sensors.Select((s, i) => new SensorDefinition(SensorId.Create(hid, s), hid, s, SensorKind.Temperature, Unit.Celsius, SensorRole.None, i)).ToList()); }
}
