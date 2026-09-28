using Mazesta.Core.Tuning; using Mazesta.Hardware.Nvidia; using Mazesta.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
namespace Mazesta.Tray;

/// <summary>
/// The GPU profiles in the tray's menu: for each NVIDIA card, stock and every profile saved for that card on the tuning page, the one it starts in
/// ticked. Picking one applies it at once and keeps it as the card's start-up profile (<see cref="GpuStartup"/>); at sign-in the tray puts every
/// card back in its start-up profile, since the driver drops these settings on each reboot. NVML is opened only when the menu is opened or a
/// profile is applied, not while the tray idles. A tuning search the app never finished (its journal is still there) means a setting was not
/// trusted: nothing is applied at start-up then, the app puts the card back to stock on its next start.
/// </summary>
internal sealed class GpuProfilesMenu
{
    private readonly string _startupFile, _profilesFile; private readonly Action<string, ToolTipIcon> _notify;
    private NvmlTuningProvider? _provider;
    public ToolStripMenuItem Menu { get; }

    public GpuProfilesMenu(AppPaths paths, Action<string, ToolTipIcon> notify)
    {
        _startupFile = GpuStartup.FileIn(paths); _profilesFile = GpuStartup.ProfilesFileIn(paths); _notify = notify;
        Menu = new ToolStripMenuItem(TrayText.GpuProfiles);
        Menu.DropDownItems.Add(new ToolStripMenuItem(TrayText.Reading) { Enabled = false });   // so the arrow shows before the first opening
        Menu.DropDownOpening += (_, _) => Build();
    }

    private IReadOnlyList<IGpuTuningDevice> Devices() => (_provider ??= new NvmlTuningProvider(NullLogger.Instance)).Devices;

    private void Build()
    {
        Menu.DropDownItems.Clear();
        IReadOnlyList<IGpuTuningDevice> devices;
        try { devices = Devices(); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { devices = []; }
        if (devices.Count == 0) { Menu.DropDownItems.Add(new ToolStripMenuItem(TrayText.NoNvidia) { Enabled = false }); return; }
        var profiles = GpuStartup.ReadProfiles(_profilesFile)?.Profiles ?? [];
        foreach (var device in devices)
        {
            if (devices.Count > 1) Menu.DropDownItems.Add(new ToolStripMenuItem(device.Name) { Enabled = false, Font = new Font(Menu.Font, FontStyle.Bold) });
            string? current = GpuStartup.For(_startupFile, device.Id);
            Menu.DropDownItems.Add(new ToolStripMenuItem(TrayText.Stock, null, (_, _) => Pick(device, null)) { Checked = current is null });
            var own = profiles.Where(p => p.GpuId == device.Id).OrderByDescending(p => p.CreatedAt).ToList();
            foreach (var p in own) Menu.DropDownItems.Add(new ToolStripMenuItem($"{p.Name}  ({TrayText.Kind(p.Kind)})", null, (_, _) => Pick(device, p)) { Checked = current == p.Name });
            if (own.Count == 0) Menu.DropDownItems.Add(new ToolStripMenuItem(TrayText.NoProfiles) { Enabled = false });
        }
    }

    private void Pick(IGpuTuningDevice device, GpuProfile? profile)
    {
        var result = profile is null ? device.Reset() : device.Apply(profile.Settings);
        if (!result.Ok) { _notify(TrayText.ProfileFailed(profile?.Name ?? TrayText.Stock, Problems(result)), ToolTipIcon.Error); return; }
        try { GpuStartup.Set(_startupFile, device.Id, profile?.Name); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _notify(TrayText.ProfileNotKept(e.Message), ToolTipIcon.Warning); return; }
        _notify(TrayText.ProfileApplied(profile?.Name ?? TrayText.Stock, device.Name), ToolTipIcon.Info);
    }

    /// <summary>At sign-in: every card back in its start-up profile. Silent when it works; a problem is a notification.</summary>
    public void ApplyAtStart()
    {
        var choices = GpuStartup.Read(_startupFile);
        if (choices.Count == 0) return;
        var doc = GpuStartup.ReadProfiles(_profilesFile);
        if (doc?.Journal is not null) { _notify(TrayText.JournalFound, ToolTipIcon.Warning); return; }
        IReadOnlyList<IGpuTuningDevice> devices;
        try { devices = Devices(); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { return; }
        foreach (var c in choices)
        {
            var device = devices.FirstOrDefault(d => d.Id == c.GpuId);
            var profile = doc?.Profiles.FirstOrDefault(p => p.GpuId == c.GpuId && p.Name == c.ProfileName);
            if (device is null) continue;   // the card is not in this machine (the app is portable): its profile is not for this one
            if (profile is null) { _notify(TrayText.ProfileMissing(c.ProfileName), ToolTipIcon.Warning); continue; }
            var result = device.Apply(profile.Settings);
            if (!result.Ok) _notify(TrayText.ProfileFailed(profile.Name, Problems(result)), ToolTipIcon.Error);
        }
    }

    private static string Problems(TuningApplyResult r) => string.Join("، ", r.Steps.Where(s => !s.Ok).Select(s => s.Error ?? s.SettingKey));
}
