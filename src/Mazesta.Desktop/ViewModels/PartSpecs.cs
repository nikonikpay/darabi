using System.Globalization; using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using Mazesta.Desktop.Localization;
namespace Mazesta.Desktop.ViewModels;

/// <summary>A small table inside a specification card (a module's speed profiles); <see cref="Highlight"/> marks the row in use, when known.</summary>
public sealed record InfoTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows, int? Highlight = null);

/// <summary>A specification card: the rows that matter up front, the rest (<see cref="SpecRow.More"/>) folded under "more", an optional table and a note.</summary>
public sealed record SpecRow(string Label, string Value, bool More = false);
public sealed record SpecCard(string Title, IReadOnlyList<SpecRow> Rows, InfoTable? Table = null, string? Note = null);

/// <summary>
/// Each part's full specification for its page, grouped into cards (the part, its caches or connection, its firmware…), from the inventory and the
/// details read from Windows, the drivers and the parts themselves. A value that was not read is left out of the card rather than shown as a
/// guess; a card whose part was not found says so in one row. Model-database facts (process node, TDP, codename) are not shown: nothing here
/// looks a model up.
/// </summary>
public static class PartSpecs
{
    private static string Na => Loc.Get("Value_NotAvailable");
    private static string YesNo(bool v) => Loc.Get(v ? "Spec_Yes" : "Spec_No");
    private static string Gb(long bytes, double unit = 1024.0 * 1024 * 1024) => (bytes / unit).ToString(bytes / unit >= 10 ? "F0" : "F1", CultureInfo.InvariantCulture) + " GB";
    private static string Tb(double bytes) => bytes >= 1e12 ? (bytes / 1e12).ToString("F2", CultureInfo.InvariantCulture) + " TB" : (bytes / 1e9).ToString("F0", CultureInfo.InvariantCulture) + " GB";
    private static string Size(long bytes) => bytes >= 1 << 20 ? $"{bytes >> 20} MB" : $"{bytes >> 10} KB";

    /// <summary>Adds a row only when there is a value: a card lists what was read.</summary>
    private static void Add(List<SpecRow> rows, string key, object? value, bool more = false, string? suffix = null)
    {
        string? text = value switch { null => null, string s => string.IsNullOrWhiteSpace(s) ? null : s.Trim(), IFormattable f => f.ToString(null, CultureInfo.InvariantCulture), _ => value.ToString() };
        if (text is not null) rows.Add(new(Loc.Get(key), suffix is null ? text : $"{text} {suffix}", more));
    }

    public static IReadOnlyList<SpecCard> For(HardwareKind kind, HardwareInventory inv, HardwareDetails d) => kind switch
    {
        HardwareKind.Cpu => Cpu(inv.Cpu, d.Cpu),
        HardwareKind.Gpu => [.. inv.Gpus.SelectMany((g, i) => Gpu(g, d.Gpus.ElementAtOrDefault(i), inv.Gpus.Count > 1 ? i + 1 : null)).DefaultIfEmpty(Missing("Dashboard_Gpu"))],
        HardwareKind.Memory => Memory(inv, d.Spd),
        HardwareKind.Storage => [.. inv.Storage.Select((s, i) => Drive(s, d.Drives.ElementAtOrDefault(i), inv.Storage.Count > 1 ? i + 1 : null)).DefaultIfEmpty(Missing("SystemInfo_Storage"))],
        HardwareKind.Network => [.. inv.NetworkAdapters.Select((n, i) => Nic(n, d.Nics.FirstOrDefault(x => x.Name == n.Name), inv.NetworkAdapters.Count > 1 ? i + 1 : null)).DefaultIfEmpty(Missing("SystemInfo_Network"))],
        HardwareKind.Motherboard => Board(inv, d.Security),
        _ => [],
    };

    /// <summary>Every part, board first, for the System Information page.</summary>
    public static IReadOnlyList<SpecCard> All(HardwareInventory inv, HardwareDetails d)
        => [.. For(HardwareKind.Motherboard, inv, d), .. For(HardwareKind.Cpu, inv, d), .. For(HardwareKind.Memory, inv, d), .. For(HardwareKind.Gpu, inv, d),
            .. For(HardwareKind.Storage, inv, d), .. For(HardwareKind.Network, inv, d)];

    private static SpecCard Missing(string titleKey) => new(Loc.Get(titleKey), [new(Loc.Get("SystemInfo_Name"), Na)]);

    // ——— CPU ———
    private static IReadOnlyList<SpecCard> Cpu(CpuInfo? c, CpuDetails? d)
    {
        if (c is null) return [Missing("Dashboard_Cpu")];
        var main = new List<SpecRow>();
        Add(main, "SystemInfo_Name", c.Name);
        Add(main, "SystemInfo_Vendor", c.Vendor == HardwareVendor.Unknown ? null : c.Vendor.ToString());
        Add(main, "Spec_CoresThreads", c.PhysicalCores is { } p && c.LogicalProcessors is { } l ? $"{p} / {l}" : null);
        Add(main, "Spec_BaseClock", d?.BaseClockMhz ?? c.MaxClockMhz, suffix: "MHz");
        Add(main, "SystemInfo_Socket", c.Socket);
        Add(main, "Spec_FamilyModel", Identifier(d?.Identifier), more: true);
        Add(main, "Spec_Microcode", d?.Microcode is { } mc ? $"0x{mc:X}" : null, more: true);
        Add(main, "Spec_BusClock", d?.BusClockMhz, more: true, suffix: "MHz");
        var cards = new List<SpecCard> { new(Loc.Get("Dashboard_Cpu"), main) };
        if (d is { Caches.Count: > 0 })
        {
            var caches = d.Caches.Select(x => new SpecRow(Loc.Format("Spec_CacheLevel", x.Level, x.Kind == "Unified" ? "" : Loc.Get("Spec_Cache_" + x.Kind)).Trim(), $"{x.Count} × {Size(x.SizeBytes)}")).ToList();
            caches.Add(new(Loc.Get("Spec_CacheTotalL3"), d.Caches.Where(x => x.Level == 3).Sum(x => x.SizeBytes * x.Count) is > 0 and var l3 ? Size(l3) : Na));
            caches.AddRange(d.Caches.Select(x => new SpecRow(Loc.Format("Spec_CacheWays", x.Level, x.Kind == "Unified" ? "" : Loc.Get("Spec_Cache_" + x.Kind)).Replace("  ", " "), $"{x.Ways}-way, {x.LineBytes} B line", true)));
            cards.Add(new(Loc.Get("Spec_Caches"), caches));
        }
        if (d is not null)
        {
            var features = new List<SpecRow>();
            if (d.InstructionSets.Count > 0) features.Add(new(Loc.Get("Spec_InstructionSets"), string.Join(", ", d.InstructionSets)));
            if (d.VirtualizationEnabled is { } v) features.Add(new(Loc.Get("Spec_Virtualization"), YesNo(v)));
            if (d.Slat is { } s) features.Add(new(Loc.Get("Spec_Slat"), YesNo(s), true));
            if (features.Count > 0) cards.Add(new(Loc.Get("Spec_Features"), features, Note: Loc.Get("Spec_Features_Note")));
        }
        return cards;
    }

    /// <summary>"AMD64 Family 23 Model 113 Stepping 0" → "Family 23 (17h) · Model 113 (71h) · Stepping 0".</summary>
    internal static string? Identifier(string? id)
    {
        var m = id is null ? null : System.Text.RegularExpressions.Regex.Match(id, @"Family (\d+) Model (\d+) Stepping (\d+)");
        if (m is not { Success: true }) return id;
        int f = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), mo = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        return $"Family {f} ({f:X}h) · Model {mo} ({mo:X}h) · Stepping {m.Groups[3].Value}";
    }

    // ——— GPU ———
    private static IReadOnlyList<SpecCard> Gpu(GpuInfo g, GpuDetails? d, int? number)
    {
        string title = number is { } n ? Loc.Format("SystemInfo_GpuN", n) : Loc.Get("Dashboard_Gpu");
        var main = new List<SpecRow>();
        Add(main, "SystemInfo_Name", g.Name);
        Add(main, "Spec_BoardVendor", d?.BoardVendor);
        Add(main, "SystemInfo_Vram", g.AdapterRamBytes is { } ram ? Gb(ram) : null);
        Add(main, "Spec_Architecture", d?.Architecture);
        Add(main, "Spec_GpuCores", d?.Cores);
        Add(main, "Spec_BusWidth", d?.BusWidthBits, suffix: "bit");
        Add(main, "Spec_Subsystem", d?.SubsystemId, more: true);
        Add(main, "Spec_ComputeCapability", d?.ComputeCapability, more: true);
        var link = new List<SpecRow>();
        Add(link, "Spec_LinkNow", Link(d?.Link?.CurrentGen, d?.Link?.CurrentWidth));
        Add(link, "Spec_LinkMax", Link(d?.Link?.MaxGen, d?.Link?.MaxWidth));
        if (d?.Bar1Bytes is { } bar && g.AdapterRamBytes is { } vram)
            Add(link, "Spec_ResizableBar", bar >= vram / 2 ? Loc.Format("Spec_Rebar_On", Size(bar)) : bar <= 512L << 20 ? Loc.Format("Spec_Rebar_Off", Size(bar)) : $"BAR1 {Size(bar)}");
        Add(link, "Spec_PciBus", d?.PciBusId, more: true);
        Add(link, "SystemInfo_DeviceId", g.PnpDeviceId, more: true);
        var firmware = new List<SpecRow>();
        Add(firmware, "SystemInfo_Driver", g.DriverVersion);
        Add(firmware, "Spec_Vbios", d?.Vbios);
        Add(firmware, "Spec_MaxCoreClock", d?.MaxCoreClockMhz, suffix: "MHz");
        Add(firmware, "Spec_MaxMemoryClock", d?.MaxMemoryClockMhz, suffix: "MHz");
        Add(firmware, "Spec_PowerLimit", d?.PowerDefaultW is { } def ? d.PowerMaxW is { } max && max != def ? $"{def} W (max {max} W)" : $"{def} W" : null);
        var cards = new List<SpecCard> { new(title, main) };
        if (link.Count > 0) cards.Add(new(Loc.Get("Spec_Connection"), link, Note: Loc.Get("Spec_Link_Note")));
        if (firmware.Count > 0) cards.Add(new(Loc.Get("Spec_DriverFirmware"), firmware));
        return cards;
    }

    /// <summary>"PCIe 4.0 x16 (16 GT/s)".</summary>
    internal static string? Link(int? gen, int? width)
        => gen is null && width is null ? null
         : $"PCIe {(gen is { } g ? $"{g}.0" : "?")} x{(width is { } w ? w.ToString(CultureInfo.InvariantCulture) : "?")}" + (PciLinkInfo.GtPerSecond(gen) is { } gt ? $" ({gt.ToString(CultureInfo.InvariantCulture)} GT/s)" : "");

    // ——— Memory ———
    private static IReadOnlyList<SpecCard> Memory(HardwareInventory inv, IReadOnlyList<SpdModule> spd)
    {
        var modules = inv.MemoryModules;
        // SPD chips are addressed by slot on the SMBus, SMBIOS lists modules by board slot: they are paired in order when the counts agree and
        // no part number disagrees; otherwise the SPD modules are shown on their own, never matched by guess.
        bool paired = spd.Count == modules.Count && spd.Zip(modules).All(p => p.First.PartNumber is null || p.Second.PartNumber is null || string.Equals(p.First.PartNumber.Trim(), p.Second.PartNumber.Trim(), StringComparison.OrdinalIgnoreCase));
        var summary = new List<SpecRow>();
        Add(summary, "Dashboard_Ram_Total", inv.TotalPhysicalMemoryBytes is { } total ? Gb(total) : null);
        Add(summary, "Dashboard_Ram_Modules", modules.Count > 0 ? string.Join(" + ", modules.GroupBy(m => m.CapacityBytes).Select(g => $"{g.Count()} × {(g.Key is { } b ? Gb(b) : Na)}")) : null);
        Add(summary, "Spec_MemoryType", modules.Select(m => m.TypeName).FirstOrDefault(t => t is not null) ?? spd.FirstOrDefault()?.MemoryType);
        int? speed = modules.Select(m => m.ConfiguredSpeedMts).FirstOrDefault(s => s is > 0);
        Add(summary, "Spec_SpeedNow", speed, suffix: "MT/s");
        if (paired && spd.Count > 0 && Ddr4Spd.Matching(spd[0], speed) is { } running) Add(summary, "Spec_ProfileNow", running.Name);
        if (spd.FirstOrDefault(s => s.Xmp.Count > 0) is { } withXmp)
        {
            // Two rows, so the Persian state and the Latin profile list each read in their own direction.
            if (speed is { } now) Add(summary, "Spec_XmpState", Loc.Get(withXmp.Xmp.Any(x => Math.Abs(x.SpeedMts - now) <= 2) ? "Spec_On" : "Spec_Off"));
            Add(summary, "Spec_XmpOffered", string.Join(", ", withXmp.Xmp.Select(x => $"{x.Name}: {x.SpeedMts} MT/s")));
        }
        Add(summary, "Spec_Voltage", modules.Select(m => m.ConfiguredVoltageMv).FirstOrDefault(v => v is > 0) is { } mv ? (mv / 1000.0).ToString("F2", CultureInfo.InvariantCulture) : null, suffix: "V");
        Add(summary, "Spec_Ecc", modules.Select(m => m.Ecc).FirstOrDefault(e => e is not null) is { } ecc ? YesNo(ecc) : null);
        var cards = new List<SpecCard> { new(Loc.Get("Dashboard_Ram"), summary, Note: spd.Count == 0 ? Loc.Get("Spec_NoSpd") : null) };
        if (paired) for (int i = 0; i < modules.Count; i++) cards.Add(Module(modules[i], spd[i], speed));
        else { cards.AddRange(modules.Select(m => Module(m, null, speed))); cards.AddRange(spd.Select(x => Module(null, x, speed))); }
        return cards;
    }

    private static SpecCard Module(MemoryModuleInfo? m, SpdModule? s, int? speedNow)
    {
        string title = m?.Slot is { } slot ? m.Bank is { } bank ? $"{slot} ({bank})" : slot : Loc.Format("Spec_SpdSlot", s?.Slot ?? 0);
        var rows = new List<SpecRow>();
        Add(rows, "SystemInfo_Manufacturer", s?.ModuleManufacturer ?? m?.Manufacturer);
        Add(rows, "Spec_PartNumber", s?.PartNumber ?? m?.PartNumber);
        Add(rows, "SystemInfo_Size", m?.CapacityBytes is { } c ? Gb(c) : s?.CapacityGb is { } g ? $"{g} GB" : null);
        Add(rows, "Spec_Ranks", m?.Ranks ?? s?.Ranks);
        Add(rows, "Spec_DramMaker", s?.DramManufacturer);
        Add(rows, "Spec_SpeedNow", m?.ConfiguredSpeedMts, suffix: "MT/s");
        Add(rows, "Spec_Organisation", s is { DeviceWidth: { } w } ? $"x{w}" + (s.DieDensityGbit is { } die ? $", {die} Gb" : "") + (s.BankGroups is > 0 and var bg ? $", {bg} bank groups" : "") : null, more: true);
        Add(rows, "Spec_ManufactureDate", s?.ManufactureWeek, more: true);
        Add(rows, "Spec_XmpVersion", s?.XmpVersion, more: true);
        InfoTable? table = null;
        if (s is not null && s.Jedec.Count + s.Xmp.Count > 0)
        {
            var profiles = s.Xmp.Concat(s.Jedec).ToList();
            var running = Ddr4Spd.Matching(s, m?.ConfiguredSpeedMts ?? speedNow);
            // Units in the headers keep the table narrow enough for a card; tRC is its own column.
            table = new([Loc.Get("Spec_Profile"), "MT/s", "CL-RCD-RP-RAS", "tRC", "V"],
                [.. profiles.Select(p => (IReadOnlyList<string>)[p.Name, p.SpeedMts.ToString(CultureInfo.InvariantCulture), $"{p.Cl}-{p.Trcd}-{p.Trp}-{p.Tras}", p.Trc.ToString(CultureInfo.InvariantCulture), p.VoltageV is { } v ? v.ToString("F2", CultureInfo.InvariantCulture) : ""])],
                running is null ? null : profiles.IndexOf(running));
        }
        string? note = s is null ? null : s.Jedec.Count + s.Xmp.Count == 0 ? Loc.Format("Spec_SpdNotDecoded", s.MemoryType) : Loc.Get("Spec_Profile_Note");
        return new(title, rows, table, note);
    }

    // ——— Storage ———
    private static SpecCard Drive(StorageDeviceInfo s, DriveDetails? d, int? number)
    {
        var rows = new List<SpecRow>();
        Add(rows, "SystemInfo_Name", s.FriendlyName);
        Add(rows, "SystemInfo_MediaType", s.MediaType);
        Add(rows, "SystemInfo_BusType", s.BusType);
        Add(rows, "Spec_LinkNow", Link(d?.Link?.CurrentGen, d?.Link?.CurrentWidth));
        Add(rows, "SystemInfo_Size", s.SizeBytes is { } b ? Gb(b, 1e9) : null);
        Add(rows, "SystemInfo_Health", SystemInfoViewModel.DriveHealth(s.HealthStatus, s.WearPercent));
        Add(rows, "Spec_PowerOnHours", d?.PowerOnHours ?? (long?)d?.Nvme?.PowerOnHours, suffix: "h");
        Add(rows, "Spec_Temperature", d?.TemperatureC is { } t ? t.ToString("F0", CultureInfo.InvariantCulture) + (d.TemperatureMaxC is { } mx ? $" °C (max {mx:F0} °C)" : " °C") : null);
        if (d?.Nvme is { } nv)
        {
            // The drive's own health log: a set warning bit and media errors are what matter first; the rest is history.
            rows.Add(new(Loc.Get("Spec_NvmeWarning"), nv.CriticalWarning == 0 ? Loc.Get("Spec_NvmeWarning_None")
                : string.Join(" · ", Enumerable.Range(0, 6).Where(bit => (nv.CriticalWarning & (1 << bit)) != 0).Select(bit => Loc.Get($"Spec_NvmeWarning_{bit}")))));
            Add(rows, "Spec_NvmeMediaErrors", nv.MediaErrors);
            Add(rows, "Spec_NvmeSpare", $"{nv.AvailableSparePercent}% ({Loc.Format("Spec_NvmeSpareThreshold", nv.SpareThresholdPercent)})");
            Add(rows, "Spec_NvmeUsed", nv.PercentageUsed, more: true, suffix: "%");
            Add(rows, "Spec_DataWritten", Tb(nv.BytesWritten), more: true);
            Add(rows, "Spec_DataRead", Tb(nv.BytesRead), more: true);
            Add(rows, "Spec_NvmeUnsafeShutdowns", nv.UnsafeShutdowns, more: true);
            Add(rows, "Spec_NvmePowerCycles", nv.PowerCycles, more: true);
            Add(rows, "Spec_NvmeErrorLog", nv.ErrorLogEntries, more: true);
        }
        Add(rows, "Spec_Wear", d?.WearPercent, more: true, suffix: "%");
        Add(rows, "Spec_UncorrectedErrors", d?.ReadErrorsUncorrected is null && d?.WriteErrorsUncorrected is null ? null : $"{d?.ReadErrorsUncorrected?.ToString(CultureInfo.InvariantCulture) ?? "-"} / {d?.WriteErrorsUncorrected?.ToString(CultureInfo.InvariantCulture) ?? "-"}", more: true);
        Add(rows, "Spec_LinkMax", Link(d?.Link?.MaxGen, d?.Link?.MaxWidth), more: true);
        Add(rows, "SystemInfo_Firmware", s.FirmwareVersion, more: true);
        Add(rows, "SystemInfo_Serial", s.SerialNumber, more: true);
        return new(number is { } n ? Loc.Format("SystemInfo_StorageN", n) : Loc.Get("SystemInfo_Storage"), rows);
    }

    // ——— Network ———
    private static SpecCard Nic(NetworkAdapterInfo n, NicDetails? d, int? number)
    {
        var rows = new List<SpecRow>();
        Add(rows, "SystemInfo_Name", n.Name);
        rows.Add(new(Loc.Get("SystemInfo_Status"), Loc.Get(n.IsUp ? "Value_Connected" : "Value_Disconnected")));
        Add(rows, "SystemInfo_LinkSpeed", n.LinkSpeedBps is { } bps ? (bps / 1_000_000.0).ToString("F0", CultureInfo.InvariantCulture) + " Mbps" : null);
        Add(rows, "SystemInfo_IpAddresses", n.IpAddresses.Count == 0 ? null : string.Join(", ", n.IpAddresses));
        Add(rows, "SystemInfo_Mac", n.MacAddress, more: true);
        Add(rows, "Spec_LinkNow", Link(d?.Link?.CurrentGen, d?.Link?.CurrentWidth), more: true);
        Add(rows, "SystemInfo_Driver", d?.Driver, more: true);
        return new(number is { } k ? Loc.Format("SystemInfo_NetworkN", k) : Loc.Get("SystemInfo_Network"), rows);
    }

    // ——— Board, firmware and platform security ———
    private static IReadOnlyList<SpecCard> Board(HardwareInventory inv, PlatformSecurity? sec)
    {
        var board = new List<SpecRow>();
        Add(board, "SystemInfo_Manufacturer", inv.Motherboard?.Manufacturer);
        Add(board, "SystemInfo_Product", inv.Motherboard?.Product);
        Add(board, "Spec_BiosVersion", inv.Bios?.Version);
        Add(board, "SystemInfo_ReleaseDate", inv.Bios?.ReleaseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(board, "SystemInfo_Version", inv.Motherboard?.Version, more: true);
        Add(board, "SystemInfo_Serial", inv.Motherboard?.SerialNumber, more: true);
        Add(board, "SystemInfo_Smbios", inv.Bios?.SmbiosVersion, more: true);
        Add(board, "Spec_BiosVendor", inv.Bios?.Vendor, more: true);
        var cards = new List<SpecCard> { new(Loc.Get("SystemInfo_Motherboard"), board.Count > 0 ? board : [new(Loc.Get("SystemInfo_Manufacturer"), Na)]) };
        if (sec is not null)
        {
            var rows = new List<SpecRow>();
            if (sec.UefiBoot is { } u) rows.Add(new(Loc.Get("Spec_BootMode"), u ? "UEFI" : "Legacy BIOS"));
            if (sec.SecureBoot is { } s) rows.Add(new(Loc.Get("Spec_SecureBoot"), Loc.Get(s ? "Spec_On" : "Spec_Off")));
            rows.Add(new(Loc.Get("Spec_Tpm"), sec.TpmEnabled == false || sec.TpmVersion is null ? Loc.Get("Spec_TpmNone") : $"TPM {sec.TpmVersion}"));
            if (sec.VbsRunning is { } v) rows.Add(new(Loc.Get("Spec_Vbs"), Loc.Get(v ? "Spec_On" : "Spec_Off"), true));
            if (sec.HvciRunning is { } h) rows.Add(new(Loc.Get("Spec_Hvci"), Loc.Get(h ? "Spec_On" : "Spec_Off"), true));
            cards.Add(new(Loc.Get("Spec_Security"), rows));
        }
        if (inv.Os is { } os)
        {
            var rows = new List<SpecRow>();
            Add(rows, "SystemInfo_Name", os.Caption); Add(rows, "SystemInfo_Build", os.BuildNumber); Add(rows, "SystemInfo_Version", os.Version, more: true); Add(rows, "SystemInfo_Architecture", os.Architecture, more: true);
            cards.Add(new(Loc.Get("SystemInfo_Os"), rows));
        }
        return cards;
    }
}
