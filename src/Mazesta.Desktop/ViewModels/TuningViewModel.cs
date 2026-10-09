using System.Collections.ObjectModel; using System.Globalization; using CommunityToolkit.Mvvm.ComponentModel; using CommunityToolkit.Mvvm.Input;
using Mazesta.Core.Inventory; using Mazesta.Core.Text; using Mazesta.Core.Tuning; using Mazesta.Desktop.Composition; using Mazesta.Desktop.Localization;
using Mazesta.Diagnostics; using Mazesta.Diagnostics.Tuning; using Mazesta.Persistence;
namespace Mazesta.Desktop.ViewModels;

/// <summary>The start-up recovery's message (see <see cref="TuningViewModel.Recover"/>), for the shell's banner; null when nothing was recovered.</summary>
public sealed record TuningRecovery(string? Message);

public sealed record GpuProfileRow(GpuProfile Profile)
{
    public string Name => Profile.Name;
    public string Kind => Loc.Get("Tuning_Kind_" + Profile.Kind);
    public GpuProfileKind KindValue => Profile.Kind;
    public string Created => Profile.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    public string Summary => TuningViewModel.Summarize(Profile.Settings);
    public string Evidence => Profile.Baseline is { } b && Profile.Tuned is { } t ? Loc.Format("Tuning_Evidence", TuningViewModel.Describe(b), TuningViewModel.Describe(t)) : "";
}

/// <summary>One finished step of an automatic search, in parts, so the page can lay it out right to left with each Latin part in its own box.</summary>
public sealed record AutoLogRow(int Step, string Kind, string Settings, string Result, bool Clean, string? Problem);

/// <summary>One scene test: the card's settings at the time, what it did in the garden scene from its fixed camera, and how that differs from the test before.</summary>
/// <summary>A listed program and the saved profile the card is put in while it is the program in use.</summary>
public sealed record GpuRuleRow(string Exe, string Name, string Profile);

public sealed record SceneTestRow(string Settings, string Result, string? Change, bool Clean, string? Problem);

/// <summary>A big live number on the card's header: what it is, and the value with its unit or the not-available text.</summary>
public sealed partial class LiveTile(string label) : ObservableObject
{
    public string Label { get; } = label;
    [ObservableProperty] private string _value = "—";
}

/// <summary>
/// Overclock and undervolt of an NVIDIA GPU through NVML. Manual settings are checked against the ranges the driver reports and refused, not
/// clamped, when outside them. The curve editor shows the card's stock voltage/frequency curve as a scan measured it and lets the technician pin a
/// voltage to a clock (offset + cap); the automatic search runs the GPU under a verified load step by step and keeps a result only when its own
/// measurements beat stock, leaving the card at stock and saving a profile the technician applies by choice. One instance for the session, like
/// Windows Tools: a quarter-hour search keeps running while the technician looks at other pages. The live readout ticks only while the page is
/// on screen.
/// </summary>
public sealed partial class TuningViewModel : ObservableObject
{
    private readonly IGpuTuningProvider _provider; private readonly JsonStore<GpuProfileDocument> _store; private readonly GpuProfileDocument _doc;
    private readonly Func<string, bool> _confirm; private readonly Action _restartToFirmware; private readonly Func<Action, object> _dispatch;
    private readonly Func<IGpuTuningDevice, IGpuLoad> _load; private readonly Func<string, Func<(DateTimeOffset At, double Volts)?>> _voltageFor; private readonly Func<string, Func<(DateTimeOffset At, double Celsius)?>> _hotSpotFor;
    private readonly Timer? _timer;   // once a second while the page is shown; its tick goes to the UI thread
    private CancellationTokenSource? _cts; private Func<(DateTimeOffset At, double Volts)?> _voltage = () => null; private Func<(DateTimeOffset At, double Celsius)?> _hotSpot = () => null;
    private (string GpuId, GpuTuningSettings Settings, LoadMeasurement Baseline)? _lastUndervolt;

    public IReadOnlyList<IGpuTuningDevice> Devices => _provider.Devices;
    public bool HasDevice => Devices.Count > 0;
    public bool HasSeveralDevices => Devices.Count > 1;
    public string Unavailable { get; }
    public bool HasUnavailable => Unavailable.Length > 0;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Ranges), nameof(CoreMin), nameof(CoreMax), nameof(MemoryMin), nameof(MemoryMax), nameof(PowerMin), nameof(PowerMax),
        nameof(FanMin), nameof(FanMax), nameof(ClockMax), nameof(HasPower), nameof(HasFan), nameof(HasCore), nameof(HasMemory))]
    private IGpuTuningDevice? _device;
    [ObservableProperty] private string _status = "";

    public LiveTile LiveCore { get; } = new(Loc.Get("Tuning_Live_Core"));
    public LiveTile LiveMemory { get; } = new(Loc.Get("Tuning_Live_Memory"));
    public LiveTile LiveVoltage { get; } = new(Loc.Get("Tuning_Live_Voltage"));
    public LiveTile LiveTemperature { get; } = new(Loc.Get("Tuning_Live_Temperature"));
    public LiveTile LivePower { get; } = new(Loc.Get("Tuning_Live_Power"));
    public LiveTile LiveFan { get; } = new(Loc.Get("Tuning_Live_Fan"));
    /// <summary>For the curve editor's live dot; NaN when not read.</summary>
    [ObservableProperty] private double _liveClockMHz = double.NaN;
    [ObservableProperty] private double _liveVoltageV = double.NaN;

    // The form. Text fields hold exactly what was typed (Persian digits included); the sliders and the curve editor read and write them through
    // the numeric properties below, so every way of setting a value ends in the same field and the same range check.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CoreOffsetValue), nameof(CurveEstimate))] private string _coreOffset = "0";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(MemoryOffsetValue))] private string _memoryOffset = "0";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CapValue), nameof(CurveEstimate))] private bool _lockClock;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CapValue), nameof(MaxClockValue), nameof(CurveEstimate))] private string _maxClock = "";
    [ObservableProperty] private bool _setPower;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PowerLimitValue))] private string _powerLimit = "";
    [ObservableProperty] private bool _manualFan;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FanValue))] private string _fanPercent = "60";
    [ObservableProperty] private string _profileName = "";

    public int CoreOffsetValue { get => Int(CoreOffset) ?? 0; set => CoreOffset = Num(value); }
    public int MemoryOffsetValue { get => Int(MemoryOffset) ?? 0; set => MemoryOffset = Num(value); }
    public int MaxClockValue { get => Int(MaxClock) ?? ClockMax; set => MaxClock = Num(value); }
    public int PowerLimitValue { get => Int(PowerLimit) ?? PowerMax; set => PowerLimit = Num(value); }
    public int FanValue { get => Int(FanPercent) ?? 60; set => FanPercent = Num(value); }
    /// <summary>The cap as the curve editor sees it: 0 is none. Setting a cap from the curve turns the cap on.</summary>
    public int CapValue
    {
        get => LockClock && Int(MaxClock) is { } c ? c : 0;
        set { if (value > 0) { MaxClock = Num(value); LockClock = true; } else LockClock = false; }
    }

    private GpuTuningLimits? L => Device?.Limits;
    public bool HasCore => L?.HasCoreOffset == true; public bool HasMemory => L?.HasMemoryOffset == true; public bool HasPower => L?.HasPowerLimit == true; public bool HasFan => L?.HasFanControl == true;
    public int CoreMin => L?.CoreOffsetMin ?? 0; public int CoreMax => L?.CoreOffsetMax ?? 0;
    public int MemoryMin => L?.MemoryOffsetMin ?? 0; public int MemoryMax => L?.MemoryOffsetMax ?? 0;
    public int PowerMin => L?.PowerLimitMinW ?? 0; public int PowerMax => L?.PowerLimitMaxW ?? 0;
    public int FanMin => L?.FanMinPercent ?? 0; public int FanMax => L?.FanMaxPercent ?? 100;
    public int ClockMax => L?.MaxLockMHz ?? 3000;

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(ResetCommand), nameof(AutoUndervoltCommand), nameof(AutoOverclockCommand), nameof(AutoOverclockPlusCommand), nameof(CancelAutoCommand),
        nameof(ApplyProfileCommand), nameof(ScanCurveCommand), nameof(SceneTestCommand))]
    private bool _isTuning;
    [ObservableProperty] private double _autoPercent;
    // The step in progress, in parts: its title is Persian, its settings Latin; the page lays them out so neither scrambles the other.
    [ObservableProperty] private string _autoStepTitle = "";
    [ObservableProperty] private string _autoStepSettings = "";
    [ObservableProperty] private string _autoStepLoad = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasAutoResult))] private string _autoResult = "";
    public bool HasAutoResult => AutoResult.Length > 0;
    public ObservableCollection<AutoLogRow> AutoLog { get; } = [];
    /// <summary>The scene tests of this session, newest last: the same picture every frame, so the frame rate, clock, power and temperature of two settings compare.</summary>
    public ObservableCollection<SceneTestRow> SceneTests { get; } = [];
    private LoadMeasurement? _lastScene; private bool _lastSceneRt;

    // The automatic profiles: the tray follows these (see GpuAutoSwitch); here they are only chosen and kept.
    private readonly string? _rulesFile; private readonly Action<string, System.Text.Json.Nodes.JsonObject?>? _usage;
    /// <summary>The profile for a full-screen game; empty: the card is left as it is during games.</summary>
    [ObservableProperty] private string _gameProfile = "";
    public ObservableCollection<GpuRuleRow> Rules { get; } = [];

    private void SaveRules()
    {
        if (_rulesFile is null) return;
        try { GpuRulesFile.Write(_rulesFile, new(GameProfile.Length == 0 ? null : GameProfile, [.. Rules.Select(r => new GpuAppRule(r.Exe, r.Name, r.Profile))])); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Status = Loc.Format("Tuning_Rules_NotSaved", e.Message); }
    }
    public void SetGameProfile(string name) { GameProfile = name; SaveRules(); }
    public void AddRule(string exe, string name, string profile)
    {
        string? file = GpuAutoRules.ExeName(exe);
        if (file is null) { Status = Loc.Get("Tuning_Rules_BadExe"); return; }
        Rules.Where(r => string.Equals(r.Exe, file, StringComparison.OrdinalIgnoreCase)).ToList().ForEach(r => Rules.Remove(r));
        Rules.Add(new(file, string.IsNullOrWhiteSpace(name) ? file : name.Trim(), profile)); SaveRules();
    }
    public void RemoveRule(string exe) { if (Rules.FirstOrDefault(r => r.Exe == exe) is { } r) { Rules.Remove(r); SaveRules(); } }
    public void SetRuleProfile(string exe, string profile) { if (Rules.FirstOrDefault(r => r.Exe == exe) is { } r) { Rules[Rules.IndexOf(r)] = r with { Profile = profile }; SaveRules(); } }
    private void LoadRules()
    {
        if (_rulesFile is null) return;
        var rules = GpuRulesFile.Read(_rulesFile);
        GameProfile = rules.GameProfile ?? ""; foreach (var a in rules.Apps) Rules.Add(new(a.Exe, a.Name, a.Profile));
    }
    public ObservableCollection<GpuProfileRow> Profiles { get; } = [];
    public bool HasProfiles => Profiles.Count > 0;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasCurve), nameof(CurveEstimate), nameof(CurveInfo))] private IReadOnlyList<VfPoint>? _curve;
    [ObservableProperty] private string _curveStatus = "";
    private DateTimeOffset? _curveMeasuredAt; private bool _curveFromDriver;
    public bool HasCurve => Curve is { Count: >= 2 };
    public string CurveInfo => Curve is { Count: >= 2 } d && _curveFromDriver
        ? Loc.Format("Tuning_Curve_Driver", Ltr($"{d.Min(p => p.VoltageV):F3}–{d.Max(p => p.VoltageV):F3} V"), Ltr($"{d.Max(p => p.ClockMHz):F0} MHz"))
        : Curve is { Count: >= 2 } c && _curveMeasuredAt is { } at
        ? Loc.Format("Tuning_Curve_Info", c.Count, Ltr($"{c.Min(p => p.VoltageV):F3}–{c.Max(p => p.VoltageV):F3} V"), Ltr($"{c.Max(p => p.ClockMHz):F0} MHz"), Ltr(at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)))
        : Loc.Get("Tuning_Curve_None");

    /// <summary>What the settings on the form do to the measured curve: the voltage the card should need at the cap (or at the top of the moved
    /// curve), next to what stock needs there. An estimate from measured points, and worded as one; outside the measured curve there is none.</summary>
    public string CurveEstimate
    {
        get
        {
            if (Curve is not { Count: >= 2 } c) return "";
            double clock = CapValue > 0 ? CapValue : c.Max(p => p.ClockMHz) + CoreOffsetValue;
            if (VfCurve.VoltageAt(c, clock, CoreOffsetValue) is not { } tuned) return Loc.Get("Tuning_Curve_OutOfRange");
            return VfCurve.VoltageAt(c, clock, 0) is { } stock ? Loc.Format("Tuning_Curve_Estimate", Ltr($"{clock:F0} MHz"), Ltr($"{tuned:F3} V"), Ltr($"{stock:F3} V"))
                : Loc.Format("Tuning_Curve_EstimateAboveStock", Ltr($"{clock:F0} MHz"), Ltr($"{tuned:F3} V"));
        }
    }

    public IReadOnlyList<InfoRow> Memory { get; private set; } = [];

    /// <summary>The profile the tray puts this card in at every sign-in (GPU settings do not survive a reboot); empty: it starts at stock.
    /// Applying a saved profile here makes it the start-up one, putting the card back to stock clears it (see <see cref="GpuStartup"/>).</summary>
    [ObservableProperty] private string _startupProfile = "";
    private readonly string? _startupFile;
    private void SetStartup(string? name)
    {
        if (_startupFile is null || Device is null) return;
        try { GpuStartup.Set(_startupFile, Device.Id, name); StartupProfile = name ?? ""; }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { Status += "  " + Loc.Format("Tuning_StartupNotSaved", e.Message); }
    }
    /// <summary>The graphics cards NVML does not cover (AMD, Intel), named so the technician knows why they are not offered.</summary>
    public string OtherGpus { get; private set; } = "";

    /// <summary>Set by <c>--selftest=</c>: nobody is at the screen, so the start of a search is confirmed and everything else (applying the finding, deleting) is declined - the run only measures.</summary>
    public static bool SelfTest { get; set; }

    public TuningViewModel(IGpuTuningProvider provider, JsonStore<GpuProfileDocument> store, InventoryCache inventory, Func<string, bool> confirm, Action restartToFirmware,
        Func<Action, object> dispatch, Func<IGpuTuningDevice, IGpuLoad> load, string? recovered, Func<string, Func<(DateTimeOffset At, double Volts)?>>? voltageFor = null, bool withTimer = true, string? startupFile = null, WorkloadGate? gate = null, string? rulesFile = null, Action<string, System.Text.Json.Nodes.JsonObject?>? usage = null, Func<string, Func<(DateTimeOffset At, double Celsius)?>>? hotSpotFor = null)
    {
        _startupFile = startupFile; _gate = gate; _rulesFile = rulesFile; _usage = usage; LoadRules();
        _provider = provider; _store = store; _doc = store.Load().Value; _confirm = text => SelfTest ? text == Loc.Get("Tuning_ConfirmAuto") || text == Loc.Get("Tuning_ConfirmPlus") : confirm(text); _restartToFirmware = restartToFirmware; _dispatch = dispatch; _load = load;
        _voltageFor = voltageFor ?? (_ => () => null); _hotSpotFor = hotSpotFor ?? (_ => () => null);
        Unavailable = provider.UnavailableReasonKey is { } key ? Loc.Get(key) + (provider.UnavailableDetail is { } d ? $" ({d})" : "") : "";
        Profiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasProfiles));
        Device = Devices.FirstOrDefault();
        if (recovered is not null) Status = recovered;
        if (withTimer) _timer = new(_ => _dispatch(RefreshLive), null, Timeout.Infinite, Timeout.Infinite);
        _ = LoadInventoryAsync(inventory);
    }

    /// <summary>Called by the view as it appears and disappears: nothing polls the card while the page is not on screen.</summary>
    public void SetVisible(bool visible) { if (visible) { RefreshLive(); _timer?.Change(1000, 1000); } else _timer?.Change(Timeout.Infinite, Timeout.Infinite); }

    partial void OnDeviceChanged(IGpuTuningDevice? value)
    {
        if (value is null) return;
        _voltage = _voltageFor(value.Name); _hotSpot = _hotSpotFor(value.Name);
        var now = value.ReadCurrent(); var l = value.Limits;
        CoreOffset = Num(now.CoreOffsetMHz); MemoryOffset = Num(now.MemoryOffsetMHz);
        LockClock = false; MaxClock = l.MaxClockMHz is { } m ? Num(m) : "";
        SetPower = now.PowerLimitW is not null; PowerLimit = Num(now.PowerLimitW ?? l.PowerLimitDefaultW ?? 0);
        ManualFan = now.FanPercent is not null; FanPercent = Num(now.FanPercent ?? 60);
        LoadProfiles();
        StartupProfile = _startupFile is null ? "" : GpuStartup.For(_startupFile, value.Id) ?? "";
        // The curve is there as the page opens: the driver's own table when it gives one, else what a scan measured on this card before.
        var saved = _doc.Curves.FirstOrDefault(c => c.GpuId == value.Id);
        var driver = (value as IGpuStockCurve)?.ReadStockCurve();
        _curveFromDriver = driver is not null; _curveMeasuredAt = saved?.MeasuredAt; Curve = driver is not null ? Thin(driver) : saved?.Points;
        OnPropertyChanged(nameof(CurveInfo));
    }

    /// <summary>The driver's table has a point every few millivolts, far more than can be told apart, or caught, on the editor: about thirty
    /// of them are kept, evenly, with the first and the last. The flat run at the bottom (the lowest clock, repeated) keeps its last point only.</summary>
    internal static IReadOnlyList<VfPoint> Thin(IReadOnlyList<VfPoint> all, int about = 32)
    {
        int first = 0; while (first + 1 < all.Count && all[first + 1].ClockMHz <= all[first].ClockMHz) first++;
        int n = all.Count - first, step = Math.Max(1, (int)Math.Ceiling(n / (double)about));
        var kept = new List<VfPoint>();
        for (int i = first; i < all.Count; i += step) kept.Add(all[i]);
        if (kept[^1] != all[^1]) kept.Add(all[^1]);
        return kept;
    }

    public string Ranges => Device?.Limits is not { } l ? "" : string.Join("   ·   ", new[]
    {
        l.HasCoreOffset ? Loc.Format("Tuning_Range_Core", Ltr($"{l.CoreOffsetMin}..{l.CoreOffsetMax} MHz")) : Loc.Get("Tuning_Range_NoCore"),
        l.HasMemoryOffset ? Loc.Format("Tuning_Range_Memory", Ltr($"{l.MemoryOffsetMin}..{l.MemoryOffsetMax} MHz")) : Loc.Get("Tuning_Range_NoMemory"),
        l.HasPowerLimit ? Loc.Format("Tuning_Range_Power", Ltr($"{l.PowerLimitMinW}..{l.PowerLimitMaxW} W"), Ltr(l.PowerLimitDefaultW is { } dw ? $"{dw} W" : "?")) : Loc.Get("Tuning_Range_NoPower"),
        l.HasFanControl ? Loc.Format("Tuning_Range_Fan", l.FanCount, Ltr($"{l.FanMinPercent}..{l.FanMaxPercent} %")) : Loc.Get("Tuning_Range_NoFan"),
    });

    internal void RefreshLive()
    {
        if (Device is null) return;
        var t = Device.ReadTelemetry(); var volts = _voltage();
        // A voltage older than a few seconds is not this moment's (the monitor paused, the sensor dropped out): it is not shown as current.
        double? v = volts is { } x && DateTimeOffset.UtcNow - x.At < TimeSpan.FromSeconds(5) ? x.Volts : null;
        static string V(double? value, string format, string unit) => value is { } x ? x.ToString(format, CultureInfo.InvariantCulture) + unit : Loc.Get("Value_NotAvailable");
        LiveCore.Value = V(t.CoreClockMHz, "F0", " MHz"); LiveMemory.Value = V(t.MemoryClockMHz, "F0", " MHz"); LiveVoltage.Value = V(v, "F3", " V");
        LiveTemperature.Value = V(t.TemperatureC, "F0", " °C"); LivePower.Value = V(t.PowerW, "F0", " W"); LiveFan.Value = V(t.FanPercent, "F0", " %");
        LiveClockMHz = t.CoreClockMHz ?? double.NaN; LiveVoltageV = v ?? double.NaN;
    }

    private static string Num(int v) => v.ToString(CultureInfo.InvariantCulture);
    private static int? Int(string? s) => int.TryParse(PersianDigits.Normalize(s ?? "").Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int v) ? v : null;

    /// <summary>The form as settings, or the resource key of the first field that is not a whole number.</summary>
    internal static (GpuTuningSettings? Settings, string? ErrorKey) Parse(string core, string memory, bool lockClock, string maxClock, bool setPower, string power, bool manualFan, string fan)
    {
        if (Int(core) is not { } c) return (null, "Tuning_Label_CoreOffset");
        if (Int(memory) is not { } m) return (null, "Tuning_Label_MemoryOffset");
        int? cap = null, watts = null, pct = null;
        if (lockClock && (cap = Int(maxClock)) is null) return (null, "Tuning_Label_MaxClock");
        if (setPower && (watts = Int(power)) is null) return (null, "Tuning_Label_PowerLimit");
        if (manualFan && (pct = Int(fan)) is null) return (null, "Tuning_Label_Fan");
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
    private void Apply() { if (FormSettings() is { } s && Report(Device!.Apply(s), "Tuning_Applied")) _usage?.Invoke("tuning.apply", Services.UsageData.Settings(s)); }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private void Reset() { if (Report(Device!.Reset(), "Tuning_ResetDone")) SetStartup(null); OnDeviceChanged(Device); }

    [RelayCommand]
    private void SaveProfile()
    {
        if (Device is null || FormSettings() is not { } s) return;
        string name = string.IsNullOrWhiteSpace(ProfileName) ? Loc.Format("Tuning_DefaultName_Manual", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)) : ProfileName.Trim();
        AddProfile(new(name, GpuProfileKind.Manual, Device.Id, Device.Name, s, DateTimeOffset.Now));
        ProfileName = ""; Status = Loc.Format("Tuning_ProfileSaved", name);
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private void ApplyProfile(GpuProfileRow row) { if (Report(Device!.Apply(row.Profile.Settings), "Tuning_Applied")) { SetStartup(row.Name); _usage?.Invoke("tuning.apply", Services.UsageData.Settings(row.Profile.Settings)); } Fill(row.Profile.Settings); }

    /// <summary>Puts a profile's settings on the form (and so on the curve) without sending them to the card.</summary>
    [RelayCommand] private void LoadProfile(GpuProfileRow row) { Fill(row.Profile.Settings); Status = Loc.Format("Tuning_ProfileLoaded", row.Name); }

    [RelayCommand]
    private void DeleteProfile(GpuProfileRow row)
    {
        if (!_confirm(Loc.Format("Tuning_ConfirmDelete", row.Name))) return;
        _doc.Profiles.Remove(row.Profile); _store.Save(_doc); LoadProfiles();
        if (row.Name == StartupProfile) SetStartup(null);
    }

    private void Fill(GpuTuningSettings s)
    {
        CoreOffset = Num(s.CoreOffsetMHz); MemoryOffset = Num(s.MemoryOffsetMHz);
        LockClock = s.MaxClockMHz is not null; if (s.MaxClockMHz is { } c) MaxClock = Num(c);
        SetPower = s.PowerLimitW is not null; if (s.PowerLimitW is { } w) PowerLimit = Num(w);
        ManualFan = s.FanPercent is not null; if (s.FanPercent is { } f) FanPercent = Num(f);
    }

    private bool Report(TuningApplyResult result, string okKey)
    {
        Status = result.Ok ? Loc.Get(okKey) : string.Join("  ", result.Steps.Where(s => !s.Ok).Select(s => Loc.Format("Tuning_Refused", Loc.Get(s.SettingKey), s.Error ?? "")));
        return result.Ok;
    }

    private void AddProfile(GpuProfile p) { _doc.Profiles.Add(p); _store.Save(_doc); LoadProfiles(); }

    private void LoadProfiles()
    {
        Profiles.Clear();
        foreach (var p in _doc.Profiles.Where(p => p.GpuId == Device?.Id).OrderByDescending(p => p.CreatedAt)) Profiles.Add(new(p));
    }

    // The curve

    /// <summary>Measures the stock curve (about a minute and a half under load, see <see cref="VfCurveScanner"/>) and keeps it for this card.</summary>
    private readonly WorkloadGate? _gate;
    /// <summary>The shared gate for a curve scan or a search, or null (and the reason in <see cref="Status"/>) while a test or benchmark runs.</summary>
    private bool TryEnter(out IDisposable? lease)
    {
        lease = _gate?.TryEnter(Workload.Tuning);
        if (_gate is null || lease is not null) return true;
        Status = Loc.Get($"Workload_Busy_{_gate.Holder ?? Workload.Tuning}");
        return false;
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task ScanCurve()
    {
        if (!_confirm(Loc.Get("Tuning_ConfirmCurve"))) return;
        if (!TryEnter(out var lease)) return;
        using var held = lease;
        var device = Device!;
        IsTuning = true; AutoPercent = 0; CurveStatus = Loc.Get("Tuning_Curve_Scanning"); Status = "";
        _cts = new CancellationTokenSource();
        var scanner = new VfCurveScanner(device, _load(device), _voltage, Journal(device.Id));
        scanner.Progress += p => _dispatch(() =>
        {
            AutoPercent = 100.0 * (p.Step - (p.Measured is null ? 1 : 0)) / p.Steps;
            CurveStatus = Loc.Format("Tuning_Curve_Step", p.Step, p.Steps, Ltr($"{p.RequestedMHz} MHz"));
        });
        VfScanResult result;
        try { result = await scanner.RunAsync(_cts.Token).ConfigureAwait(true); }
        finally { IsTuning = false; AutoPercent = 0; }
        if (!result.Ok) { CurveStatus = Loc.Get(result.ReasonKey!) + (result.Detail is { } d ? $" ({d})" : ""); return; }
        var curve = new GpuCurve(device.Id, DateTimeOffset.Now, result.Points);
        _doc.Curves.RemoveAll(c => c.GpuId == device.Id); _doc.Curves.Add(curve); _store.Save(_doc);
        // The driver's table, when it gives one, is the whole curve; the scan is only what the card did under load, which stops at the voltage it
        // holds while working. It is kept in the file, but does not replace the complete curve on the chart.
        bool driverKept = _curveFromDriver;
        _curveMeasuredAt = curve.MeasuredAt;
        if (!driverKept) Curve = curve.Points;
        OnPropertyChanged(nameof(CurveInfo));
        CurveStatus = Loc.Get(driverKept ? "Tuning_Curve_DoneDriver" : "Tuning_Curve_Done");
    }

    // The automatic search

    [RelayCommand(CanExecute = nameof(CanChange))]
    private Task AutoUndervolt()
    {
        if (!_confirm(Loc.Get("Tuning_ConfirmAuto"))) return Task.CompletedTask;
        return RunAuto(new UndervoltSearch(Device!.Limits, new AutoTuneOptions()), GpuProfileKind.Undervolt);
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private Task AutoOverclock()
    {
        if (!_confirm(Loc.Get("Tuning_ConfirmAuto"))) return Task.CompletedTask;
        var device = Device!;
        var (start, baseline) = OverclockStart(_doc.Profiles, device.Id, _lastUndervolt);
        return RunAuto(new OverclockSearch(device.Limits, new AutoTuneOptions { RayTraceScene = _load(device).SupportsRayTracing }, start, baseline), GpuProfileKind.Overclock, start is not null);
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private Task AutoOverclockPlus()
    {
        if (!_confirm(Loc.Get("Tuning_ConfirmPlus"))) return Task.CompletedTask;
        var device = Device!;
        var (start, baseline) = OverclockStart(_doc.Profiles, device.Id, _lastUndervolt);
        return RunAuto(new OverclockSearch(device.Limits, AutoTuneOptions.Plus() with { RayTraceScene = _load(device).SupportsRayTracing }, start, baseline, plus: true), GpuProfileKind.OverclockPlus, start is not null);
    }

    /// <summary>Where an automatic overclock starts: this session's undervolt of the card, else its newest saved undervolt whose stock measurement
    /// came from the current load (an older, lighter load's score would make any new run look faster), else stock - measured first.</summary>
    internal static (GpuTuningSettings? Start, LoadMeasurement? Baseline) OverclockStart(IEnumerable<GpuProfile> profiles, string gpuId, (string GpuId, GpuTuningSettings Settings, LoadMeasurement Baseline)? session)
        => session is { } uv && uv.GpuId == gpuId ? (uv.Settings, uv.Baseline)
        : profiles.Where(p => p.GpuId == gpuId && p.Kind == GpuProfileKind.Undervolt && p.Baseline is not null && p.LoadVersion == LoadMeasurement.CurrentLoadVersion).MaxBy(p => p.CreatedAt) is { } saved
            ? (saved.Settings, saved.Baseline) : (null, null);

    [RelayCommand(CanExecute = nameof(IsTuning))] private void CancelAuto() => _cts?.Cancel();

    public const int SceneSecondsMin = 20, SceneSecondsMax = 600;
    private static readonly TimeSpan SceneTestSettle = TimeSpan.FromSeconds(12);
    /// <summary>How long the scene test runs, in seconds (the first 12 are warm-up and not counted), and whether it draws the ray-traced (DXR 1.1) picture.</summary>
    [ObservableProperty] private int _sceneSeconds = 40;
    [ObservableProperty] private bool _sceneRayTracing;
    partial void OnSceneSecondsChanged(int value) { int fit = Math.Clamp(value, SceneSecondsMin, SceneSecondsMax); if (fit != value) SceneSeconds = fit; }

    /// <summary>Runs the garden scene from its fixed camera on the card as it is now (whatever has been applied) and keeps the figures beside those of the earlier
    /// tests: change the profile, run it again, and see what the frame rate, the temperature and the power did.</summary>
    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task SceneTest()
    {
        if (!TryEnter(out var lease)) return;
        using var held = lease;
        var device = Device!;
        IsTuning = true; AutoPercent = 0; Status = ""; AutoStepTitle = Loc.Get("Tuning_SceneTest_Running"); AutoStepSettings = Summarize(device.ReadCurrent()); AutoStepLoad = Loc.Get(SceneRayTracing ? "Tuning_Load_SceneRt" : "Tuning_Load_Scene");
        _cts = new CancellationTokenSource(); bool rayTraced = SceneRayTracing; var length = TimeSpan.FromSeconds(Math.Clamp(SceneSeconds, SceneSecondsMin, SceneSecondsMax));
        var tuner = new GpuAutoTuner(device, _load(device), Journal(device.Id), voltage: _voltage, hotSpot: _hotSpot);
        tuner.Progress += p => _dispatch(() => AutoPercent = p.Fraction * 100);
        LoadMeasurement m; string? error;
        try { (m, error) = await tuner.MeasureCurrentAsync(GpuLoadKind.Scene, length, SceneTestSettle, _cts.Token, rayTraced).ConfigureAwait(true); }
        catch (OperationCanceledException) { Status = Loc.Get("Tuning_Out_Cancelled"); return; }
        finally { IsTuning = false; AutoPercent = 0; AutoStepTitle = AutoStepSettings = AutoStepLoad = ""; }
        string settings = Summarize(device.ReadCurrent()) + (rayTraced ? " · " + Loc.Get("Tuning_SceneTest_RtTag") : ""), result = Describe(m, GpuLoadKind.Scene); string? change = null;
        if (m.Clean && error is null && _lastScene is { } before && before.Throughput > 0 && _lastSceneRt == rayTraced)
            change = Ltr($"{(m.Throughput / before.Throughput - 1) * 100:+0.0;-0.0}% FPS" + (m.AverageTemperatureC is { } t && before.AverageTemperatureC is { } bt ? $" · {t - bt:+0.0;-0.0} °C" : "") + (m.AveragePowerW is { } w && before.AveragePowerW is { } bw ? $" · {w - bw:+0;-0} W" : ""));
        SceneTests.Add(new(settings, result, change, m.Clean && error is null, m.Clean && error is null ? null : Loc.Get(m.DeviceLost ? "Tuning_Lost" : "Tuning_Errors") + (error is { } e ? $" ({e})" : "")));
        if (m.Clean && error is null) { _lastScene = m; _lastSceneRt = rayTraced; }
        _usage?.Invoke("tuning.scene", Services.UsageData.Scene(device.Name, device.ReadCurrent(), m, SceneTests[^1].Problem));
    }

    [RelayCommand] private void ClearSceneTests() { SceneTests.Clear(); _lastScene = null; }

    /// <param name="fromProfile">The search starts from a saved undervolt, so its 3D-scene baseline is that profile, not stock.</param>
    private async Task RunAuto(IAutoTuneSearch search, GpuProfileKind kind, bool fromProfile = false)
    {
        if (!TryEnter(out var lease)) return;
        using var held = lease;
        var device = Device!;
        IsTuning = true; AutoLog.Clear(); AutoResult = ""; AutoPercent = 0; Status = "";
        _cts = new CancellationTokenSource();
        var tuner = new GpuAutoTuner(device, _load(device), Journal(device.Id), voltage: _voltage, hotSpot: _hotSpot);
        tuner.Progress += p => _dispatch(() =>
        {
            AutoPercent = p.Fraction * 100;
            AutoStepTitle = Loc.Format("Tuning_Auto_StepTitle", p.StepNumber, Loc.Get("Tuning_Step_" + p.Step.Kind));
            AutoStepSettings = StepSettings(p.Step); AutoStepLoad = Loc.Get("Tuning_Load_" + p.Step.Load);
        });
        tuner.StepFinished += s => _dispatch(() => AutoLog.Add(new(s.StepNumber, Loc.Get("Tuning_Step_" + s.Step.Kind), StepSettings(s.Step), Describe(s.Measurement, s.Step.Load),
            s.Measurement.Clean, s.Measurement.Clean && s.Error is null ? null : Loc.Get(s.Measurement.DeviceLost ? "Tuning_Lost" : "Tuning_Errors") + (s.Error is { } e ? $" ({e})" : ""))));
        AutoTuneOutcome outcome;
        try { outcome = await tuner.RunAsync(search, _cts.Token).ConfigureAwait(true); }
        finally { IsTuning = false; AutoPercent = 100; AutoStepTitle = AutoStepSettings = AutoStepLoad = ""; }
        AutoResult = Loc.Get(outcome.ReasonKey) + (outcome.Detail is { } d ? $" ({d})" : "");
        _usage?.Invoke("tuning.auto", Services.UsageData.Auto(kind.ToString(), device.Name, outcome));
        if (outcome.Baseline is { PeakClockMHz: not null } f) AutoResult += "\n" + Loc.Format("Tuning_Factory", Ltr(Fmt(f.MedianClockMHz, " MHz")), Ltr(Fmt(f.PeakClockMHz, " MHz")), Ltr(Fmt(f.AveragePowerW, " W")), Ltr(Fmt(f.PeakPowerW, " W")), Ltr(f.AverageVoltageV is { } v ? $"{v:F3} V" : "—"));
        if (outcome.Baseline is { } b && outcome.Tuned is { } t) AutoResult += "\n" + Loc.Format("Tuning_Evidence", Describe(b), Describe(t));
        if (outcome.SceneBaseline is { } sb) AutoResult += "\n" + Loc.Format(fromProfile ? "Tuning_Evidence_SceneStart" : "Tuning_Evidence_Scene", Describe(sb, GpuLoadKind.Scene), outcome.SceneTuned is { } st ? Describe(st, GpuLoadKind.Scene) : "—");
        if (outcome.MemoryBaseline is { } mb && outcome.MemoryTuned is { } mt) AutoResult += "\n" + Loc.Format("Tuning_Evidence_Memory", Describe(mb, GpuLoadKind.Memory), Describe(mt, GpuLoadKind.Memory));
        if (outcome is not { Verdict: AutoTuneVerdict.Improved, Settings: { } found }) return;
        if (kind == GpuProfileKind.Undervolt) _lastUndervolt = (device.Id, found, outcome.Baseline!);
        string name = Loc.Format(kind == GpuProfileKind.Undervolt ? "Tuning_DefaultName_Undervolt" : kind == GpuProfileKind.OverclockPlus ? "Tuning_DefaultName_OverclockPlus" : "Tuning_DefaultName_Overclock", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        AddProfile(new(name, kind, device.Id, device.Name, found, DateTimeOffset.Now, outcome.Baseline, outcome.Tuned, LoadMeasurement.CurrentLoadVersion));
        AutoResult += "\n" + Loc.Format("Tuning_ProfileSaved", name);
        if (_confirm(Loc.Format("Tuning_ConfirmApplyFound", name, Summarize(found)))) { if (Report(device.Apply(found), "Tuning_Applied")) SetStartup(name); Fill(found); }
    }

    private static string StepSettings(TuneStep s) => Summarize(s.Settings) + (s.RayTraced && s.Load == GpuLoadKind.Scene ? " · " + Loc.Get("Tuning_SceneTest_RtTag") : "");

    private Action<GpuTuningSettings?> Journal(string gpuId) => settings =>
    {
        lock (_doc) { _doc.Journal = settings is null ? null : new(gpuId, settings, DateTimeOffset.Now); _store.Save(_doc); }
    };

    /// <summary>Keeps a number with its sign and unit in reading order inside a Persian (right-to-left) sentence: without the embedding, "+135 MHz"
    /// comes out as "MHz 135+". Left-to-right marks on both sides make the whole chunk one left-to-right run (WPF's text layout did not keep
    /// the LRE/PDF embedding in the page's text blocks, so plain marks are used).</summary>
    internal static string Ltr(string s) => "‎" + s + "‎";
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

    private static string Fmt(double? v, string unit) => v is { } x ? x.ToString("F0", CultureInfo.InvariantCulture) + unit : "—";

    public static string Describe(LoadMeasurement m, GpuLoadKind load = GpuLoadKind.Compute)
    {
        static string V(double? v, string unit) => Fmt(v, unit);
        string peak = m.PeakClockMHz is { } pk && m.MedianClockMHz is { } md && pk >= md + 15 ? $" (↑{pk:F0})" : "", volt = m.AverageVoltageV is { } vv ? $" · {vv:F3} V" : "";
        string score = load switch { GpuLoadKind.Compute => m.Throughput.ToString("F0", CultureInfo.InvariantCulture) + " Gop/s", GpuLoadKind.Scene => m.Throughput.ToString("F1", CultureInfo.InvariantCulture) + " FPS", _ => m.Throughput.ToString("F0", CultureInfo.InvariantCulture) + " GB/s" };
        string hot = m.MaxHotSpotC is { } hs ? $" · HOT {hs:F0} °C" : "";
        return Ltr($"{V(m.MedianClockMHz, " MHz")}{peak}{volt} · {V(m.AveragePowerW, " W")} · {V(m.AverageTemperatureC, " °C")}{hot} · {score}");
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
        Memory = MemoryRows(inv.MemoryModules);
        OnPropertyChanged(nameof(Memory)); OnPropertyChanged(nameof(OtherGpus));
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
