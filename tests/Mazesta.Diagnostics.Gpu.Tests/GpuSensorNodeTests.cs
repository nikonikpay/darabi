using Xunit;
using Mazesta.Core.Hardware; using Mazesta.Core.Providers; using Mazesta.Core.Time; using Mazesta.Diagnostics.Evidence; using Mazesta.Monitoring; using Microsoft.Extensions.Logging.Abstractions;
namespace Mazesta.Diagnostics.Gpu.Tests;

/// <summary>A run on one card reads that card's sensors: on a machine with an iGPU and a discrete card, the free VRAM that sizes the VRAM
/// test must be the tested card's, and a reading that stopped arriving is not current.</summary>
public class GpuSensorNodeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } = T0; }
    private sealed class TwoGpus(string second = "NVIDIA GeForce RTX 4070", string first = "Intel(R) UHD Graphics 770") : ISensorProvider
    {
        public List<HardwareNode> Nodes { get; } = [Gpu("gpu/igpu", first), Gpu("gpu/rtx", second)];
        public string Name => "fake"; public ProviderStatus Status => ProviderStatus.Ready(2); public IReadOnlyList<HardwareNode> Hardware => Nodes;
        public event Action<ProviderStatus>? StatusChanged { add { } remove { } }
        public void Start() { }
        public PollResult Poll(PollRequest r) => new([new(Nodes[0].Sensors[0].Id, 900, r.Now, DataQuality.Ok, Name), new(Nodes[1].Sensors[0].Id, 11000, r.Now, DataQuality.Ok, Name)],
            Nodes.ToDictionary(n => n.Id, n => NodeStatus.Healthy(r.Now)));
        public void Dispose() { }
        private static HardwareNode Gpu(string id, string name)
        { var hid = new HardwareId(id); return new(hid, HardwareKind.Gpu, HardwareVendor.Unknown, name, null, true, [new(SensorId.Create(hid, "smalldata/free"), hid, "GPU Memory Free", SensorKind.SmallData, Unit.Megabyte, SensorRole.GpuVramFree, 0)]); }
    }

    private static (PollingEngine Engine, Clock Clock) Build(TwoGpus? gpus = null)
    {
        var clock = new Clock(); var engine = new PollingEngine(gpus ?? new TwoGpus(), clock, new MonitoringOptions(), new BoundedEventLog(clock, NullLogger.Instance));
        engine.PrepareForManualTicks(); engine.TickOnce();
        return (engine, clock);
    }

    [Fact] public void The_tested_card_reads_its_own_free_vram_not_the_other_cards()
    {
        var (engine, clock) = Build();
        Assert.Equal(11000, SensorEvidence.Latest(engine, HardwareKind.Gpu, SensorRole.GpuVramFree, clock.UtcNow, GpuDevices.SensorNode(engine, "NVIDIA GeForce RTX 4070")));
        Assert.Equal(900, SensorEvidence.Latest(engine, HardwareKind.Gpu, SensorRole.GpuVramFree, clock.UtcNow, GpuDevices.SensorNode(engine, "Intel(R) UHD Graphics 770")));
    }

    [Fact] public void With_several_cards_and_no_name_match_nothing_is_read()
        => Assert.Null(SensorEvidence.Latest(Build().Engine, HardwareKind.Gpu, SensorRole.GpuVramFree, T0, GpuDevices.SensorNode(Build().Engine, "AMD Radeon RX 7800 XT")));

    [Fact] public void Two_cards_of_one_name_are_not_told_apart_by_it_so_neither_is_read()
    {
        var (engine, clock) = Build(new TwoGpus("NVIDIA GeForce RTX 4070", "NVIDIA GeForce RTX 4070"));
        Assert.Null(SensorEvidence.Latest(engine, HardwareKind.Gpu, SensorRole.GpuVramFree, clock.UtcNow, GpuDevices.SensorNode(engine, "NVIDIA GeForce RTX 4070")));
    }

    [Fact] public void A_reading_older_than_the_limit_is_not_current()
    {
        var (engine, clock) = Build();
        clock.UtcNow = T0.AddSeconds(30);   // no poll since
        Assert.Null(SensorEvidence.Latest(engine, HardwareKind.Gpu, SensorRole.GpuVramFree, clock.UtcNow, GpuDevices.SensorNode(engine, "NVIDIA GeForce RTX 4070")));
    }
}
