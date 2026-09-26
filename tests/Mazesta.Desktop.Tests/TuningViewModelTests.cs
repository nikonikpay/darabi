using System.IO; using System.Windows; using Mazesta.Core.Inventory; using Mazesta.Core.Providers; using Mazesta.Core.Tuning; using Mazesta.Desktop.Composition; using Mazesta.Desktop.Localization;
using Mazesta.Desktop.ViewModels; using Mazesta.Diagnostics.Tuning; using Mazesta.Persistence; using Microsoft.Extensions.Logging.Abstractions; using Xunit;
namespace Mazesta.Desktop.Tests;

public sealed class TuningViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-tuning-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
    private JsonStore<GpuProfileDocument> Store() => new(Path.Combine(_dir, "gpu-profiles.json"), new SchemaMigrator([]), GpuProfileDocument.CurrentSchemaVersion, NullLogger.Instance);

    internal sealed class Card(string id = "GPU-A") : IGpuTuningDevice
    {
        public List<GpuTuningSettings> Applied { get; } = []; public int Resets;
        public string Id => id; public string Name => "NVIDIA GeForce RTX 3090";
        public GpuTuningLimits Limits { get; } = new(-1000, 1000, -2000, 6000, 2100, 100, 365, 350, 2, 30, 100);
        public GpuTelemetry ReadTelemetry() => new(DateTimeOffset.UtcNow, 1695, 9751, 55, 136, 30);
        public GpuTuningSettings ReadCurrent() => GpuTuningSettings.Stock;
        public TuningApplyResult Apply(GpuTuningSettings s) { Applied.Add(s); return new([]); }
        public TuningApplyResult Reset() { Resets++; return new([]); }
    }
    internal sealed class Provider(params IGpuTuningDevice[] devices) : IGpuTuningProvider
    {
        public IReadOnlyList<IGpuTuningDevice> Devices => devices;
        public string? UnavailableReasonKey => devices.Length == 0 ? "Tuning_Unavailable_NoNvidia" : null;
        public string? UnavailableDetail => null;
    }
    internal sealed class Inv : IInventoryProvider { public Task<HardwareInventory> ReadAsync(CancellationToken ct) => Task.FromResult(HardwareInventory.Empty); }
    internal sealed class NoLoad : IGpuLoad { public LoadRunResult Run(GpuLoadKind k, TimeSpan d, TimeSpan s, CancellationToken ct) => new(0, 0, false, null); }

    private TuningViewModel Vm(IGpuTuningProvider provider, JsonStore<GpuProfileDocument>? store = null, bool answer = true)
        => new(provider, store ?? Store(), new InventoryCache(new Inv(), NullLogger<InventoryCache>.Instance), _ => answer, () => { }, a => { a(); return null!; }, _ => new NoLoad(), null, withTimer: false);

    [Fact] public void The_form_accepts_persian_digits_and_negative_offsets_and_names_the_field_that_is_not_a_number()
    {
        Assert.Equal(new GpuTuningSettings(-50, 500, 1905, null, 70), TuningViewModel.Parse("-50", "۵۰۰", true, "1905", false, "x", true, "۷۰").Settings);
        Assert.Equal("Tuning_Label_MaxClock", TuningViewModel.Parse("0", "0", true, "fast", false, "", false, "").ErrorKey);
        Assert.Null(TuningViewModel.Parse("0", "0", false, "fast", false, "", false, "").ErrorKey);   // an unchecked field is not read
    }

    [Fact] public void A_value_outside_the_driver_range_is_refused_and_never_sent_to_the_card()
    {
        var card = new Card(); var vm = Vm(new Provider(card));
        vm.CoreOffset = "1500"; vm.ApplyCommand.Execute(null);
        Assert.Empty(card.Applied); Assert.Contains("-1000..1000 MHz", vm.Status);
        vm.CoreOffset = "120"; vm.LockClock = true; vm.MaxClock = "1905"; vm.ApplyCommand.Execute(null);
        Assert.Equal([new GpuTuningSettings(120, 0, 1905, null, null)], card.Applied);
    }

    [Fact] public void Profiles_are_only_offered_on_the_card_they_were_made_on()
    {
        var store = Store(); var doc = new GpuProfileDocument();
        doc.Profiles.Add(new("mine", GpuProfileKind.Undervolt, "GPU-A", "x", new(90, 0, 1905, null, null), DateTimeOffset.Now));
        doc.Profiles.Add(new("other machine", GpuProfileKind.Overclock, "GPU-B", "x", new(0, 500, null, null, null), DateTimeOffset.Now));
        store.Save(doc);
        var vm = Vm(new Provider(new Card("GPU-A")), store);
        Assert.Equal(["mine"], vm.Profiles.Select(p => p.Name));
    }

    [Fact] public void Saving_a_manual_profile_keeps_it_across_a_restart()
    {
        var store = Store(); var vm = Vm(new Provider(new Card()), store);
        vm.CoreOffset = "60"; vm.ProfileName = "quiet"; vm.SaveProfileCommand.Execute(null);
        var saved = Assert.Single(store.Load().Value.Profiles);
        Assert.Equal(("quiet", GpuProfileKind.Manual, "GPU-A", 60), (saved.Name, saved.Kind, saved.GpuId, saved.Settings.CoreOffsetMHz));
    }

    [Fact] public void A_journal_left_by_a_run_that_never_came_back_resets_that_card_and_is_cleared()
    {
        var store = Store(); var card = new Card();
        store.Save(new GpuProfileDocument { Journal = new("GPU-A", new(240, 0, 1905, null, null), DateTimeOffset.Now) });
        var message = TuningViewModel.Recover(store, new Provider(card));
        Assert.Equal(1, card.Resets); Assert.Null(store.Load().Value.Journal);
        Assert.Contains("+240 MHz", message);
        Assert.Null(TuningViewModel.Recover(store, new Provider(card)));   // once only
    }

    [Fact] public void Without_an_nvidia_card_the_page_says_why()
    {
        var vm = Vm(new Provider());
        Assert.False(vm.HasDevice); Assert.Equal(Loc.Get("Tuning_Unavailable_NoNvidia"), vm.Unavailable);
    }

    [Fact] public void Memory_rows_show_both_speeds_and_mark_a_missing_one_unavailable()
    {
        var rows = TuningViewModel.MemoryRows([new("DIMMA1", 32L << 30, "Corsair", "CMK64GX5M2X6800C32 ", 6800, null)]);
        Assert.Equal("DIMMA1 · CMK64GX5M2X6800C32", rows[0].Label);
        Assert.Equal(Loc.Format("Tuning_Memory_Speeds", TuningViewModel.Ltr("6800 MT/s"), Loc.Get("Value_NotAvailable")), rows[0].Value);
    }
}
