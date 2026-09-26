using System.Collections.ObjectModel; using System.Globalization; using CommunityToolkit.Mvvm.ComponentModel; using CommunityToolkit.Mvvm.Input;
using Mazesta.Core.Inventory; using Mazesta.Core.Text; using Mazesta.Core.Tuning; using Mazesta.Desktop.Composition; using Mazesta.Desktop.Localization;
using Mazesta.Diagnostics.Tuning; using Mazesta.Persistence;
namespace Mazesta.Desktop.ViewModels;

/// <summary>The start-up recovery's message (see <see cref="TuningViewModel.Recover"/>), for the shell's banner; null when nothing was recovered.</summary>
public sealed record TuningRecovery(string? Message);

public sealed record GpuProfileRow(GpuProfile Profile)
{
    public string Name => Profile.Name;
    public string Kind => Loc.Get("Tuning_Kind_" + Profile.Kind);
    public string Summary => TuningViewModel.Summarize(Profile.Settings);
    public string Evidence => Profile.Baseline is { } b && Profile.Tuned is { } t ? Loc.Format("Tuning_Evidence", TuningViewModel.Describe(b), TuningViewModel.Describe(t)) : "";
}

/// <summary>
/// Overclock and undervolt (GPU through NVIDIA's NVML; CPU and memory profiles explained, not changed). Manual settings are checked against the
/// ranges the driver reports and refused, not clamped, when outside them. The automatic search runs the GPU under a verified load step by step and
/// keeps a result only when its own measurements beat stock; it leaves the card at stock and saves a profile the technician applies by choice.
/// One instance for the session, like Windows Tools: a quarter-hour search keeps running while the technician looks at other pages. The live
/// readout ticks only while the page is on screen.
/// </summary>
public sealed partial class TuningViewModel : ObservableObject
{
    private readonly IGpuTuningProvider _provider; private readonly JsonStore<GpuProfileDocument> _store; private readonly GpuProfileDocument _doc;
    private readonly Func<string, bool> _confirm; private readonly Action _restartToFirmware; private readonly Func<Action, object> _dispatch;
    private readonly Func<IGpuTuningDevice, IGpuLoad> _load; private readonly System.Windows.Threading.DispatcherTimer? _timer;
    private CancellationTokenSource? _cts;
    private (string GpuId, GpuTuningSettings Settings, LoadMeasurement Baseline)? _lastUndervolt;

    public IReadOnlyList<IGpuTuningDevice> Devices => _provider.Devices;
    public bool HasDevice => Devices.Count > 0;
    public string Unavailable { get; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Ranges))] private IGpuTuningDevice? _device;
    [ObservableProperty] private string _live = "";
    [ObservableProperty] private string _status = "";

    [ObservableProperty] private string _coreOffset = "0";
    [ObservableProperty] private string _memoryOffset = "0";
    [ObservableProperty] private bool _lockClock;
    [ObservableProperty] private string _maxClock = "";
    [ObservableProperty] private bool _setPower;
    [ObservableProperty] private string _powerLimit = "";
    [ObservableProperty] private bool _manualFan;
    [ObservableProperty] private string _fanPercent = "60";
    [ObservableProperty] private string _profileName = "";

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(ResetCommand), nameof(AutoUndervoltCommand), nameof(AutoOverclockCommand), nameof(CancelAutoCommand), nameof(ApplyProfileCommand))]
    private bool _isTuning;
    [ObservableProperty] private double _autoPercent;
    [ObservableProperty] private string _autoStatus = "";
    [ObservableProperty] private string _autoResult = "";
    public ObservableCollection<string> AutoLog { get; } = [];
    public ObservableCollection<GpuProfileRow> Profiles { get; } = [];

    public string OtherGpus { get; private set; } = "";
    public string CpuName { get; private set; } = "";
    public string CpuNote { get; private set; } = "";
    public IReadOnlyList<InfoRow> Memory { get; private set; } = [];

    public TuningViewModel(IGpuTuningProvider provider, JsonStore<GpuProfileDocument> store, InventoryCache inventory, Func<string, bool> confirm, Action restartToFirmware,
        Func<Action, object> dispatch, Func<IGpuTuningDevice, IGpuLoad> load, string? recovered, bool withTimer = true)
    {
        _provider = provider; _store = store; _doc = store.Load().Value; _confirm = confirm; _restartToFirmware = restartToFirmware; _dispatch = dispatch; _load = load;
        Unavailable = provider.UnavailableReasonKey is { } key ? Loc.Get(key) + (provider.UnavailableDetail is { } d ? $" ({d})" : "") : "";
        Device = Devices.FirstOrDefault();
        if (recovered is not null) Status = recovered;
        if (withTimer) { _timer = new() { Interval = TimeSpan.FromSeconds(1) }; _timer.Tick += (_, _) => RefreshLive(); }
        _ = LoadInventoryAsync(inventory);
    }

    /// <summary>Called by the view as it appears and disappears: nothing polls the card while the page is not on screen.</summary>
    public void SetVisible(bool visible) { if (visible) { RefreshLive(); _timer?.Start(); } else _timer?.Stop(); }

    partial void OnDeviceChanged(IGpuTuningDevice? value)
    {
        if (value is null) return;
        var now = value.ReadCurrent(); var l = value.Limits;
        CoreOffset = Num(now.CoreOffsetMHz); MemoryOffset = Num(now.MemoryOffsetMHz);
        LockClock = false; MaxClock = l.MaxClockMHz is { } m ? Num(m) : "";
        SetPower = now.PowerLimitW is not null; PowerLimit = Num(now.PowerLimitW ?? l.PowerLimitDefaultW ?? 0);
        ManualFan = now.FanPercent is not null; FanPercent = Num(now.FanPercent ?? 60);
        LoadProfiles();
    }

    public string Ranges => Device?.Limits is not { } l ? "" : string.Join("   ·   ", new[]
    {
        l.HasCoreOffset ? Loc.Format("Tuning_Range_Core", Ltr($"{l.CoreOffsetMin}..{l.CoreOffsetMax} MHz")) : Loc.Get("Tuning_Range_NoCore"),
        l.HasMemoryOffset ? Loc.Format("Tuning_Range_Memory", Ltr($"{l.MemoryOffsetMin}..{l.MemoryOffsetMax} MHz")) : Loc.Get("Tuning_Range_NoMemory"),
        l.HasPowerLimit ? Loc.Format("Tuning_Range_Power", Ltr($"{l.PowerLimitMinW}..{l.PowerLimitMaxW} W"), Ltr(l.PowerLimitDefaultW is { } dw ? $"{dw} W" : "?")) : Loc.Get("Tuning_Range_NoPower"),
        l.HasFanControl ? Loc.Format("Tuning_Range_Fan", l.FanCount, Ltr($"{l.FanMinPercent}..{l.FanMaxPercent} %")) : Loc.Get("Tuning_Range_NoFan"),
    });

    private void RefreshLive()
    {
        if (Device is null) return;
        var t = Device.ReadTelemetry();
        static string V(double? v, string unit) => v is { } x ? x.ToString("F0", CultureInfo.InvariantCulture) + unit : "—";
        Live = $"{V(t.CoreClockMHz, " MHz")}   ·   {Loc.Get("Tuning_Live_Memory")} {V(t.MemoryClockMHz, " MHz")}   ·   {V(t.TemperatureC, " °C")}   ·   {V(t.PowerW, " W")}   ·   {Loc.Get("Tuning_Live_Fan")} {V(t.FanPercent, " %")}";
    }

    private static string Num(int v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>The form as settings, or the resource key of the first field that is not a whole number.</summary>
    internal static (GpuTuningSettings? Settings, string? ErrorKey) Parse(string core, string memory, bool lockClock, string maxClock, bool setPower, string power, bool manualFan, string fan)
    {
        static int? I(string s) => int.TryParse(PersianDigits.Normalize(s ?? "").Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int v) ? v : null;
        if (I(core) is not { } c) return (null, "Tuning_Label_CoreOffset");
        if (I(memory) is not { } m) return (null, "Tuning_Label_MemoryOffset");
        int? cap = null, watts = null, pct = null;
        if (lockClock && (cap = I(maxClock)) is null) return (null, "Tuning_Label_MaxClock");
        if (setPower && (watts = I(power)) is null) return (null, "Tuning_Label_PowerLimit");
        if (manualFan && (pct = I(fan)) is null) return (null, "Tuning_Label_Fan");
        return (new(c, m, cap, watts, pct), null);
    }

    private GpuTuningSettings? FormSettings()
    {
        var (settings, error) = Parse(CoreOffset, MemoryOffset, LockClock, MaxClock, SetPower, PowerLimit, ManualFan, FanPercent);
        if (settings is null) { Status = Loc.Format("Tuning_NotANumber", Loc.Get(error!)); return null; }
        var problems = Device!.Limits.Check(settings);
        if (problems.Count > 0) { Status = string.Join("  ", problems.Select(p => Loc.Format(p.Key, Ltr(p.Range + UnitOf(p.Key))))); return null; }
        return settings;
    }

    private bool CanChange() => Device is not null && !IsTuning;

    [RelayCommand(CanExecute = nameof(CanChange))]
    private void Apply() { if (FormSettings() is { } s) Report(Device!.Apply(s), "Tuning_Applied"); }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private void Reset() { Report(Device!.Reset(), "Tuning_ResetDone"); OnDeviceChanged(Device); }

    [RelayCommand]
    private void SaveProfile()
    {
        if (Device is null || FormSettings() is not { } s) return;
        string name = string.IsNullOrWhiteSpace(ProfileName) ? Loc.Format("Tuning_DefaultName_Manual", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)) : ProfileName.Trim();
        AddProfile(new(name, GpuProfileKind.Manual, Device.Id, Device.Name, s, DateTimeOffset.Now));
        ProfileName = ""; Status = Loc.Format("Tuning_ProfileSaved", name);
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private void ApplyProfile(GpuProfileRow row) { Report(Device!.Apply(row.Profile.Settings), "Tuning_Applied"); Fill(row.Profile.Settings); }

    [RelayCommand]
    private void DeleteProfile(GpuProfileRow row)
    {
        if (!_confirm(Loc.Format("Tuning_ConfirmDelete", row.Name))) return;
        _doc.Profiles.Remove(row.Profile); _store.Save(_doc); LoadProfiles();
    }

    private void Fill(GpuTuningSettings s)
    {
        CoreOffset = Num(s.CoreOffsetMHz); MemoryOffset = Num(s.MemoryOffsetMHz);
        LockClock = s.MaxClockMHz is not null; if (s.MaxClockMHz is { } c) MaxClock = Num(c);
        SetPower = s.PowerLimitW is not null; if (s.PowerLimitW is { } w) PowerLimit = Num(w);
        ManualFan = s.FanPercent is not null; if (s.FanPercent is { } f) FanPercent = Num(f);
    }

    private void Report(TuningApplyResult result, string okKey)
        => Status = result.Ok ? Loc.Get(okKey) : string.Join("  ", result.Steps.Where(s => !s.Ok).Select(s => Loc.Format("Tuning_Refused", Loc.Get(s.SettingKey), s.Error ?? "")));

    private void AddProfile(GpuProfile p) { _doc.Profiles.Add(p); _store.Save(_doc); LoadProfiles(); }

    private void LoadProfiles()
    {
        Profiles.Clear();
        foreach (var p in _doc.Profiles.Where(p => p.GpuId == Device?.Id).OrderByDescending(p => p.CreatedAt)) Profiles.Add(new(p));
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private Task AutoUndervolt()
    {
        if (!_confirm(Loc.Get("Tuning_ConfirmAuto"))) return Task.CompletedTask;
        return RunAuto(new UndervoltSearch(Device!.Limits, new AutoTuneOptions()), GpuProfileKind.Undervolt);
    }

    /// <summary>Builds on this session's undervolt, or on the newest saved undervolt profile of this card (its offset, clock and stock measurement);
    /// with neither, starts from stock and measures it first.</summary>
    [RelayCommand(CanExecute = nameof(CanChange))]
    private Task AutoOverclock()
    {
        if (!_confirm(Loc.Get("Tuning_ConfirmAuto"))) return Task.CompletedTask;
        var device = Device!;
        (GpuTuningSettings? start, LoadMeasurement? baseline) = _lastUndervolt is { } uv && uv.GpuId == device.Id ? (uv.Settings, uv.Baseline)
            : _doc.Profiles.Where(p => p.GpuId == device.Id && p.Kind == GpuProfileKind.Undervolt && p.Baseline is not null).MaxBy(p => p.CreatedAt) is { } saved ? (saved.Settings, saved.Baseline) : (null, null);
        return RunAuto(new OverclockSearch(device.Limits, new AutoTuneOptions(), start, baseline), GpuProfileKind.Overclock);
    }

    [RelayCommand(CanExecute = nameof(IsTuning))] private void CancelAuto() => _cts?.Cancel();

    private async Task RunAuto(IAutoTuneSearch search, GpuProfileKind kind)
    {
        var device = Device!;
        IsTuning = true; AutoLog.Clear(); AutoResult = ""; AutoPercent = 0; Status = "";
        _cts = new CancellationTokenSource();
        var tuner = new GpuAutoTuner(device, _load(device), Journal(device.Id));
        tuner.Progress += p => _dispatch(() => { AutoPercent = p.Fraction * 100; AutoStatus = Loc.Format("Tuning_Auto_Step", p.StepNumber, Loc.Get("Tuning_Step_" + p.Step.Kind), Summarize(p.Step.Settings), Loc.Get("Tuning_Load_" + p.Step.Load)); });
        tuner.StepFinished += s => _dispatch(() => AutoLog.Add($"{s.StepNumber}. {Loc.Get("Tuning_Step_" + s.Step.Kind)} · {Summarize(s.Step.Settings)} → {Describe(s.Measurement, s.Step.Load)}"
            + (s.Measurement.Clean ? "" : " · " + Loc.Get(s.Measurement.DeviceLost ? "Tuning_Lost" : "Tuning_Errors")) + (s.Error is { } e ? $" ({e})" : "")));
        AutoTuneOutcome outcome;
        try { outcome = await tuner.RunAsync(search, _cts.Token).ConfigureAwait(true); }
        finally { IsTuning = false; AutoPercent = 100; AutoStatus = ""; }
        AutoResult = Loc.Get(outcome.ReasonKey) + (outcome.Detail is { } d ? $" ({d})" : "");
        if (outcome.Baseline is { } b && outcome.Tuned is { } t) AutoResult += "\n" + Loc.Format("Tuning_Evidence", Describe(b), Describe(t));
        if (outcome.MemoryBaseline is { } mb && outcome.MemoryTuned is { } mt) AutoResult += "\n" + Loc.Format("Tuning_Evidence_Memory", Describe(mb, GpuLoadKind.Memory), Describe(mt, GpuLoadKind.Memory));
        if (outcome is not { Verdict: AutoTuneVerdict.Improved, Settings: { } found }) return;
        if (kind == GpuProfileKind.Undervolt) _lastUndervolt = (device.Id, found, outcome.Baseline!);
        string name = Loc.Format(kind == GpuProfileKind.Undervolt ? "Tuning_DefaultName_Undervolt" : "Tuning_DefaultName_Overclock", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        AddProfile(new(name, kind, device.Id, device.Name, found, DateTimeOffset.Now, outcome.Baseline, outcome.Tuned));
        AutoResult += "\n" + Loc.Format("Tuning_ProfileSaved", name);
        if (_confirm(Loc.Format("Tuning_ConfirmApplyFound", name, Summarize(found)))) { Report(device.Apply(found), "Tuning_Applied"); Fill(found); }
    }

    private Action<GpuTuningSettings?> Journal(string gpuId) => settings =>
    {
        lock (_doc) { _doc.Journal = settings is null ? null : new(gpuId, settings, DateTimeOffset.Now); _store.Save(_doc); }
    };

    /// <summary>Keeps a number with its sign and unit in reading order inside a Persian (right-to-left) sentence: without the embedding, "+135 MHz"
    /// comes out as "MHz 135+". Left-to-right marks on both sides make the whole chunk one left-to-right run (WPF's text layout did not keep
    /// the LRE/PDF embedding in the page's text blocks, so plain marks are used).</summary>
    internal static string Ltr(string s) => "\u200E" + s + "\u200E";
    private static string UnitOf(string key) => key switch { "Tuning_Bad_PowerLimit" => " W", "Tuning_Bad_Fan" => " %", _ => " MHz" };

    public static string Summarize(GpuTuningSettings s)
    {
        if (s.IsStock) return Loc.Get("Tuning_Stock");
        var parts = new List<string>();
        if (s.CoreOffsetMHz != 0) parts.Add(Loc.Format("Tuning_Sum_Core", Ltr(s.CoreOffsetMHz.ToString("+0;-0", CultureInfo.InvariantCulture) + " MHz")));
        if (s.MaxClockMHz is { } c) parts.Add(Loc.Format("Tuning_Sum_Cap", Ltr($"{c} MHz")));
        if (s.MemoryOffsetMHz != 0) parts.Add(Loc.Format("Tuning_Sum_Memory", Ltr(s.MemoryOffsetMHz.ToString("+0;-0", CultureInfo.InvariantCulture) + " MHz")));
        if (s.PowerLimitW is { } w) parts.Add(Loc.Format("Tuning_Sum_Power", Ltr($"{w} W")));
        if (s.FanPercent is { } f) parts.Add(Loc.Format("Tuning_Sum_Fan", Ltr($"{f} %")));
        return string.Join(" · ", parts);
    }

    public static string Describe(LoadMeasurement m, GpuLoadKind load = GpuLoadKind.Compute)
    {
        static string V(double? v, string unit) => v is { } x ? x.ToString("F0", CultureInfo.InvariantCulture) + unit : "—";
        string score = load == GpuLoadKind.Compute ? m.Throughput.ToString("F0", CultureInfo.InvariantCulture) + " Gop/s" : m.Throughput.ToString("F0", CultureInfo.InvariantCulture) + " GB/s";
        return Ltr($"{V(m.MedianClockMHz, " MHz")} · {V(m.AveragePowerW, " W")} · {V(m.AverageTemperatureC, " °C")} · {score}");
    }

    /// <summary>At start-up: a journal left behind means an automatic search never came back from the setting it names (a freeze, a reboot, the app
    /// killed). That card is put back to stock and the journal cleared; the returned text tells the technician. Null when there was nothing to recover.</summary>
    internal static string? Recover(JsonStore<GpuProfileDocument> store, IGpuTuningProvider provider)
    {
        var doc = store.Load().Value;
        if (doc.Journal is not { } j) return null;
        var device = provider.Devices.FirstOrDefault(d => d.Id == j.GpuId);
        bool reset = device?.Reset().Ok == true;
        doc.Journal = null; store.Save(doc);
        return Loc.Format(reset ? "Tuning_Recovered" : "Tuning_RecoveredNoReset", Summarize(j.Settings), j.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
    }

    [RelayCommand]
    private void RestartToFirmware() { if (_confirm(Loc.Get("Tuning_ConfirmFirmware"))) _restartToFirmware(); }

    private async Task LoadInventoryAsync(InventoryCache inventory)
    {
        HardwareInventory inv;
        try { inv = await inventory.GetAsync().ConfigureAwait(true); } catch (Exception e) when (e is System.Management.ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { return; }
        var others = inv.Gpus.Where(g => g.Name is { } n && !n.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)).Select(g => g.Name!).ToList();
        OtherGpus = others.Count == 0 ? "" : Loc.Format("Tuning_OtherGpus", string.Join("، ", others));
        CpuName = inv.Cpu?.Name?.Trim() ?? Loc.Get("Value_NotAvailable");
        CpuNote = Loc.Get(inv.Cpu?.Vendor switch { Core.Hardware.HardwareVendor.Intel => "Tuning_Cpu_Intel", Core.Hardware.HardwareVendor.Amd => "Tuning_Cpu_Amd", _ => "Tuning_Cpu_Other" });
        Memory = MemoryRows(inv.MemoryModules);
        OnPropertyChanged(nameof(OtherGpus)); OnPropertyChanged(nameof(CpuName)); OnPropertyChanged(nameof(CpuNote)); OnPropertyChanged(nameof(Memory));
    }

    /// <summary>Each module's configured speed next to the speed its SMBIOS entry reports. Which of the two an XMP/EXPO kit reports as "Speed" varies
    /// by board, so no verdict on whether the profile is on is drawn from them.</summary>
    internal static IReadOnlyList<InfoRow> MemoryRows(IReadOnlyList<MemoryModuleInfo> modules)
    {
        static string S(int? v) => v is { } x ? Ltr(x.ToString(CultureInfo.InvariantCulture) + " MT/s") : Loc.Get("Value_NotAvailable");
        if (modules.Count == 0) return [new(Loc.Get("Tuning_Memory"), Loc.Get("Value_NotAvailable"))];
        return [.. modules.Select(m => new InfoRow($"{m.Slot ?? "?"} · {m.PartNumber?.Trim() ?? ""}", Loc.Format("Tuning_Memory_Speeds", S(m.ConfiguredSpeedMts), S(m.SpeedMts))))];
    }
}
