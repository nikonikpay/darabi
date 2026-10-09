using System.Text.RegularExpressions; using Mazesta.Desktop.Localization;
namespace Mazesta.Desktop.Services;

/// <summary>One line of a test's detail for the page: its text, and whether it is still the executor's own English (shown left to right).</summary>
public sealed record DetailLine(string Text, bool Latin);

/// <summary>
/// A test's detail, as its executor wrote it (one English line, parts joined by "; "), turned into separate lines in the user's language. Only
/// the parts whose form is known are worded again, with the same numbers; a part that is not recognised is kept exactly as written, on a line
/// of its own, so nothing is dropped and nothing is guessed. The report keeps the executor's original line.
/// </summary>
public static partial class TestDetailText
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["GPU load"] = "Detail_L_GpuLoad", ["CPU load"] = "Detail_L_CpuLoad", ["GPU core"] = "Detail_L_GpuCore", ["GPU temperature"] = "Detail_L_GpuCore", ["GPU hot spot"] = "Detail_L_GpuHotSpot",
        ["GPU power"] = "Detail_L_GpuPower", ["GPU clock"] = "Detail_L_GpuClock", ["GPU memory clock"] = "Detail_L_GpuMemClock", ["CPU package power"] = "Detail_L_CpuPower", ["CPU temperature"] = "Detail_L_CpuTemp", ["VRAM in use"] = "Detail_L_VramUsed",
    };
    private static readonly Dictionary<string, string> Counts = new(StringComparer.Ordinal)
    {
        ["dispatches"] = "Detail_C_Dispatches", ["threads"] = "Detail_C_Threads", ["iterations"] = "Detail_C_Iterations", ["passes"] = "Detail_C_Passes", ["frames"] = "Detail_C_Frames",
        ["blocks"] = "Detail_C_Blocks", ["transforms"] = "Detail_C_Transforms", ["core visits"] = "Detail_C_CoreVisits", ["multiplies"] = "Detail_C_Multiplies", ["check frames"] = "Detail_C_CheckFrames",
        ["sent"] = "Detail_C_Sent",
    };

    /// <summary>A sentence whose form is known: its pattern (the whole part), and how it is said with the same numbers.</summary>
    /// <remarks><see cref="Tail"/> names a group that is somebody else's English (Windows' message, a driver's error): it follows on a line of
    /// its own, left to right, instead of sitting inside the worded sentence.</remarks>
    private sealed record Rule(Regex Pattern, Func<Match, string> Say, int Tail)
    {
        public IEnumerable<DetailLine> Lines(Match m) { yield return new(Say(m), false); if (Tail > 0 && m.Groups[Tail].Value is { Length: > 0 } tail) yield return new(tail, true); }
    }
    private static Rule R(string pattern, string key, Func<Match, object[]>? args = null, int tail = 0)
        => new(new Regex("^" + pattern + "$", RegexOptions.CultureInvariant), m => Loc.Format(key, args?.Invoke(m) ?? [.. m.Groups.Values.Skip(1).Select(g => (object)g.Value)]), tail);
    private static string G(Match m, int i) => m.Groups[i].Value;

    /// <summary>Details that are one sentence (often with a "; " of their own): why a test could not run, or that it broke.</summary>
    private static readonly Lazy<Rule[]> Whole = new(() =>
    [
        R(@"Duration must be positive\.", "Detail_W_Duration"),
        R(@"The test itself failed \((.+)\); nothing is known about the part from this run\.", "Detail_W_Error"),
        R(@"Only (\d+) MiB of free RAM is available above the OS reserve; at least (\d+) MiB is needed\.", "Detail_W_LittleRam"),
        R(@"Only (\d+) MiB of VRAM can safely be tested; at least (\d+) MiB is needed\.", "Detail_W_LittleVram"),
        R(@"A (\d+)x\d+ matrix needs (\d+) MiB; only (\d+) MiB of RAM is free\.", "Detail_W_LinpackRam"),
        R(@"Too little free RAM for even one FFT worker \((\d+) MiB each, above the reserve Windows needs\)\.", "Detail_W_FftRam"),
        R(@"No network adapter is connected\.", "Detail_W_NoAdapter"),
        R(@"Windows reported no processor cores\.", "Detail_W_NoCores"),
        R(@"No drive reported a SMART health status\.", "Detail_W_NoSmart"),
        R(@"Windows did not report drive health: (.+)", "Detail_W_SmartFailed", tail: 1),
        R(@"no internet connection", "Detail_W_NoInternet"),
        R(@"The driver refused to allocate any VRAM buffer\.", "Detail_W_NoVramBuffer"),
        R(@"No LAN partner was given: start the LAN partner on another computer and enter its address\.", "Detail_W_NoLanPartner"),
        R(@"The LAN test needs at least 3 seconds\.", "Detail_W_LanShort"),
        R(@"The CPU does not support '(.+)'\.", "Detail_W_CpuLacks"),
        R(@"'(.+)' is not an IP address and did not resolve\.", "Detail_W_NotAnAddress"),
        R(@"'(.+)' is not an IPv4 address \(optionally with :port\)\.", "Detail_W_NotIpv4"),
        R(@"The LAN partner at (\S+) did not answer \((\w+)\): is the partner switched on there, and does its firewall let port (\d+) in\?", "Detail_W_LanSilent"),
        R(@"The connection to (\S+) was lost during the test \((.+)\); nothing is concluded about the network from a run that did not finish\.", "Detail_W_LanLost"),
        R(@"Pulse and gap lengths must be positive\.", "Detail_W_Pulse"),
        R(@"The first rendered frame is not a valid image \(non-finite values or no contrast\)\.", "Detail_W_BadFrame"),
    ]);

    private static readonly Lazy<Rule[]> Parts = new(() =>
    [
        // ——— processor ———
        R(@"matrix load (\d+)x\d+, (\d+) fixed input sets, every product checked in full against a precomputed checksum", "Detail_Cpu_Matrix"),
        R(@"full load in (\d+) stages of (\d+) s on (\d+) threads, every block checked against a value worked out in advance", "Detail_Cpu_Stress"),
        R(@"(matrix|integer|hash): (\d+) blocks in (\d+) s, (\d+) wrong", "Detail_Cpu_StressStage", m => [Loc.Get("Test_Cpu_Stage_" + char.ToUpperInvariant(G(m, 1)[0]) + G(m, 1)[1..]), G(m, 2), G(m, 3), G(m, 4)]),
        R(@"part load: (\d+)% for (\d+) s, then (\d+)% for (\d+) s, in turn", "Detail_Cpu_StressSwing"),
        R(@"part load: held at (\d+)%", "Detail_Cpu_StressHeld"),
        R(@"memory filled: (\d+) systems, (\d+) MiB \((.+)\)", "Detail_Cpu_LinpackFilled"),
        R(@"single-core cycling over (\d+) physical cores, ([\d.]+) s each, (variable|steady) load, matrix (\d+)x\d+", "Detail_Cpu_Cycle", m => [G(m, 1), G(m, 2), Loc.Get("Detail_Load_" + G(m, 3)), G(m, 4)]),
        R(@"radix-2 complex FFT, N=(\d+) and N=(\d+), (\d+) threads", "Detail_Cpu_Fft"),
        R(@"radix-2 complex FFT, N=(\d+) and N=(\d+), (\d+) threads of (\d+) \(free RAM held \d+ workers of (\d+) MiB\)", "Detail_Cpu_FftFewer"),
        R(@"checked against a direct DFT \(relative error ([^)]+)\), an inverse round trip and Parseval, then bit for bit", "Detail_Cpu_FftChecks"),
        R(@"SHA-256 and Deflate round trip on (\d+) threads \(SHA extensions: (yes|no)\), every block checked against a precomputed hash", "Detail_Cpu_Hash", m => [G(m, 1), Loc.Get("Detail_" + G(m, 2))]),
        R(@"integer load \(multiply, 64-bit divide, shift, rotate, xor, branch\) on (\d+) threads, every block checked against a precomputed checksum", "Detail_Cpu_Integer"),
        R(@"vector FMA stress, (AVX-512|AVX2 \+ FMA|SSE2), (\d+) threads, every lane checked against a scalar reference", "Detail_Cpu_Vector"),
        R(@"Linpack \(LU with partial pivoting\), n=(\d+) \((\d+) MiB\), (\d+) threads", "Detail_Cpu_Linpack"),
        R(@"worst scaled residual (\S+) \(bound 16\)", "Detail_Cpu_Residual"),
        R(@"ran on (\d+) of (\d+) logical processors", "Detail_Cpu_Coverage"),
        R(@"ran on (\d+) of (\d+) logical processors \((.+)\)", "Detail_Cpu_CoverageSplit", m => [G(m, 1), G(m, 2), Coverage(G(m, 3))]),
        R(@"every core tested", "Detail_Cpu_EveryCore"),
        R(@"not tested: core\(s\) (.+) - the result covers only the others", "Detail_Cpu_NotTested"),
        R(@"wrong results on (.+)", "Detail_Cpu_WrongOn", m => [G(m, 1).Replace("core ", Loc.Get("Detail_Word_Core") + " ", StringComparison.Ordinal)]),
        R(@"Windows refused pinning to core\(s\) (.+)", "Detail_Cpu_Unpinned"),
        R(@"compressed to ([\d.]+)%", "Detail_Cpu_Compressed"),
        R(@"([\d,.]+) GFLOPS( \(.+\))?", "Detail_Rate_Gflops"),
        R(@"([\d,.]+) Gop/s \(?integer\)?", "Detail_Gops"),
        R(@"([\d,.]+) MB/s of input", "Detail_Rate_Input"),
        // ——— memory ———
        R(@"RAM pattern test", "Detail_Mem_Pattern"),
        R(@"tested=(\d+) MiB", "Detail_Mem_Tested"),
        R(@"passes=(\d+) of (\d+) patterns", "Detail_Mem_Passes"),
        R(@"passes=(\d+) of (\d+) patterns \(not every pattern ran; a longer run covers them all\)", "Detail_Mem_PassesShort"),
        R(@"moving inversions ×(\d+), block move ×(\d+), stride ×(\d+)", "Detail_Mem_Algorithms"),
        R(@"([\d,.]+) GB/s", "Detail_Rate_GbS"),
        R(@"covers only the RAM Windows let the test have, addressed by buffer offset, not physical address or slot", "Detail_Mem_Covers"),
        R(@"addressed by buffer offset, not physical address or slot", "Detail_Mem_Offsets"),
        R(@"Bit fade", "Detail_Mem_BitFade"),
        R(@"held (\d+) MiB as all ones, then all zeros, untouched for (.+?) - shorter than the (\d+) s a leaking cell needs to show", "Detail_Mem_HeldShort", m => [G(m, 1), Seconds(G(m, 2)), G(m, 3)]),
        R(@"held (\d+) MiB as all ones, then all zeros, untouched for (.+?)", "Detail_Mem_Held", m => [G(m, 1), Seconds(G(m, 2))]),
        R(@"run it longer for a result", "Detail_Mem_RunLonger"),
        R(@"(\d+) of (\d+) blocks locked in RAM", "Detail_Mem_Locked"),
        R(@"(\d+) of (\d+) blocks locked in RAM - Windows would not lock the rest, so they may have been paged out while waiting and their result says nothing about the RAM", "Detail_Mem_LockedSome"),
        R(@"first mismatch in buffer block (\d+) \(offset (\d+) MiB\) (?:during|while verifying) '(.+)'", "Detail_Mem_Mismatch"),
        R(@"first faded bytes in buffer block (\d+) \(offset (\d+) MiB\) after holding '(.+)'", "Detail_Mem_Faded"),
        // ——— storage ———
        R(@"sequential unbuffered write-through I/O", "Detail_Sto_Sequential"),
        R(@"random 4K QD1 unbuffered write-through", "Detail_Sto_Random"),
        R(@"write ([\d.]+) MB/s", "Detail_Sto_Write"),
        R(@"read ([\d.]+) MB/s", "Detail_Sto_Read"),
        R(@"verified (\d+) MiB in (\d+) pass\(es\)", "Detail_Sto_Verified"),
        R(@"(\d+) verified blocks", "Detail_Sto_Blocks"),
        R(@"write ([\d.]+) IOPS, latency (.+)", "Detail_Sto_WriteIops", m => [G(m, 1), Latency(G(m, 2))]),
        R(@"read ([\d.]+) IOPS, latency (.+)", "Detail_Sto_ReadIops", m => [G(m, 1), Latency(G(m, 2))]),
        R(@"(\d+) short read\(s\)", "Detail_Sto_ShortReads"),
        R(@"(\d+) byte\(s\) read back wrong", "Detail_Sto_WrongBytes"),
        R(@"I/O error while exercising the drive: (.+)", "Detail_Sto_IoError", tail: 1),
        R(@"(.+) NVMe log: critical warning (.+), media errors (\d+), spare (\d+)% \(threshold (\d+)%\), used (\d+)%, unsafe shutdowns (\d+), error-log entries (\d+)", "Detail_Sto_NvmeLog",
            m => [G(m, 1), G(m, 2) == "none" ? Loc.Get("Detail_None") : G(m, 2), G(m, 3), G(m, 4), G(m, 5), G(m, 6), G(m, 7), G(m, 8)]),
        R(@"(.+): (Healthy|Warning|Unhealthy|Unknown)((?:, .+)?)", "Detail_Sto_Drive", m => [G(m, 1), Loc.Get("Detail_Drive_" + G(m, 2)), DriveFacts(G(m, 3))]),
        R(@"NVMe health log could not be read after the test \((\w+)\)", "Detail_Log_NvmeUnread"),
        R(@"WHEA log could not be read \((\w+)\)", "Detail_Log_WheaUnread"),
        R(@"storage event log could not be read \((\w+)\)", "Detail_Log_StorageUnread"),
        R(@"WHEA logged (\d+) hardware error record\(s\) during this test \(event ids ([\d, ]+)\): (.+)", "Detail_Log_Whea", tail: 3),
        R(@"Windows logged (\d+) storage event\(s\) during this test: (.+)", "Detail_Log_Storage", tail: 2),
        R(@"not proof of a failing drive on its own - check the cable, controller, driver and power too", "Detail_Log_StorageNote"),
        // ——— network ———
        R(@"ICMP to (\S+)", "Detail_Net_Icmp"),
        R(@"lost=([\d.]+)%", "Detail_Net_Lost"),
        R(@"latency min/avg/max ([\d.]+)/([\d.]+)/([\d.]+) ms", "Detail_Net_Latency"),
        R(@"jitter ([\d.]+) ms", "Detail_Net_Jitter"),
        R(@"jitter not measured \(fewer than 2 replies\)", "Detail_Net_NoJitter"),
        R(@"no reply at all - the target or a firewall may drop ICMP, or there is no route", "Detail_Net_NoReply"),
        R(@"try another target before blaming the network card", "Detail_Net_TryAnother"),
        R(@"links: (.+)", "Detail_Net_Links"),
        R(@"data moved one way only", "Detail_Net_OneWay"),
        R(@"LAN test with (\S+): upload (\d+) Mbit/s \((\d+) MB sent, (\d+) MB received by the partner, (\d+) wrong\), download (\d+) Mbit/s \((\d+) MB, (\d+) wrong\)", "Detail_Net_Lan"),
        R(@"round trip at rest (.+), while downloading (.+)", "Detail_Net_RoundTrip", m => [Latency(G(m, 1)), Latency(G(m, 2))]),
        // ——— graphics card ———
        R(@"ray-traced scene (\d+)x(\d+), (\d+) spheres, (\d+) bounces, on (.+)", "Detail_Gpu_Render"),
        R(@"([\d,.]+) MPixel/s \(Mazesta's own scene, not a commercial score\)", "Detail_Gpu_MPixel"),
        R(@"VRAM pattern test on (.+)", "Detail_Gpu_Vram"),
        R(@"tested=(\d+) MiB in (\d+) buffers", "Detail_Gpu_VramTested"),
        R(@"GPU error during (?:the run|rendering|the VRAM test): (.+)", "Detail_Gpu_Error", tail: 1),
        R(@"(DirectX Raytracing|Direct3D 12 \+ ray tracing|Direct3D 12) Persian garden drawn at (\d+)x(\d+) \(window (\d+)x(\d+)\) on (.+)", "Detail_Gpu_Scene"),
        R(@"([\d,]+) triangles in ([\d,]+) objects, centre model '(.+)'", "Detail_Gpu_SceneModel"),
        R(@"ray traced: camera ray, a shadow ray to the moon and to each of (\d+) lamps in reach, reflections and refraction up to 4 bounces", "Detail_Gpu_SceneRays"),
        R(@"ray traced: camera ray, a shadow ray to the sun or the moon and to each of (\d+) lamps in reach once they are lit, reflections and refraction up to 4 bounces", "Detail_Gpu_SceneRaysDay"),
        R(@"load level (\d+): ([\d.]+) M triangles · ([\d,]+) objects · (\d+) lamps · (?:ray-traced shadows \((\d+) a light\) and reflections|shadows (\d+))(?: · MSAA (\d+)×)?(?: · pool reflection 1/(\d+))?", "Detail_Gpu_SceneLoadLine", m => [G(m, 1), SceneWork(m)]),
        R(@"load level (\d+): (.+)", "Detail_Gpu_SceneLoad", tail: 2),
        R(@"([\d,.]+) FPS average", "Detail_Gpu_FpsAvg"),
        R(@"([\d,.]+) FPS lowest half-second", "Detail_Gpu_FpsLow"),
        R(@"the test window was closed before the time was up", "Detail_Gpu_WindowClosed"),
        // ——— what the processor and the RAM did during a graphics run, and what Mazesta itself cost ———
        R(@"CPU clock peak ([\d.]+) MHz", "Detail_Cpu_ClockPeak"),
        R(@"CPU clock avg ([\d.]+) MHz", "Detail_Cpu_ClockAvg"),
        R(@"CPU load avg ([\d.]+) %", "Detail_Cpu_LoadAvg"),
        R(@"CPU power peak ([\d.]+) W", "Detail_Cpu_PowerPeak"),
        R(@"RAM speed (\d+) MT/s", "Detail_Ram_Speed"),
        R(@"RAM CAS latency \(profile at that speed\) (\d+) CL", "Detail_Ram_Cas"),
        R(@"Mazesta's own load: processor ([\d.]+) cores busy on average \(peak ([\d.]+); (\d+) % of the whole processor\), RAM (\d+) MB on average \(peak (\d+) MB\)", "Detail_Footprint"),
        // ——— Windows' own tools ———
        R(@"(sfc\.exe|dism\.exe) (.+): (Healthy|Repaired|Damaged|Unknown) \(exit code (-?\d+)\)", "Detail_Win_Tool", m => [G(m, 1), G(m, 2), Loc.Get("Detail_Win_" + G(m, 3)), G(m, 4)]),
    ]);

    /// <summary>A key measured figure of a run, for the big numbers at the top of a result: what it is, its value with the unit, and a note (the average beside a highest).</summary>
    public sealed record DetailFigure(string Name, string Value, string? Note);
    public sealed record DetailView(IReadOnlyList<DetailFigure> Figures, IReadOnlyList<DetailLine> Lines);

    /// <summary>The order the key figures stand in: the heat, clock and power of the part under test first, then the frame rate, then the rest.</summary>
    private static readonly string[] FigureOrder = ["GPU temperature", "GPU core", "GPU hot spot", "GPU clock", "GPU power", "GPU load", "fps avg", "fps low", "GPU memory clock", "CPU temperature", "CPU package power", "CPU load", "VRAM in use"];

    /// <summary>The detail as the page shows it: the figures that matter (a measured highest or average) apart, and every other line as <see cref="Lines"/> words it.</summary>
    public static DetailView View(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return new([], []);
        string all = detail.Trim();
        if (Whole.Value.Any(r => r.Pattern.IsMatch(all))) return new([], Lines(detail));
        var figures = new List<(int Rank, DetailFigure Figure)>(); var lines = new List<DetailLine>();
        foreach (var part in Split(all)) { if (Figure(part) is { } f) figures.Add(f); else lines.AddRange(Line(part)); }
        return new([.. figures.OrderBy(f => f.Rank).Select(f => f.Figure)], lines);
    }

    private static (int, DetailFigure)? Figure(string part)
    {
        if (Stat().Match(part) is { Success: true } s && Labels.TryGetValue(s.Groups[1].Value, out var label) && Array.IndexOf(FigureOrder, s.Groups[1].Value) is var rank and >= 0)
        {
            string unit = s.Groups[3].Value.Trim(); bool max = s.Groups[4].Success;
            return (rank, new(Loc.Format(max ? "Detail_Fig_Max" : "Detail_Fig_Avg", Loc.Get(label)), $"{(max ? s.Groups[4].Value : s.Groups[2].Value)} {unit}", max ? Loc.Format("Detail_Fig_Note", s.Groups[2].Value, unit, s.Groups[5].Value) : null));
        }
        if (FpsAverage().Match(part) is { Success: true } a) return (Array.IndexOf(FigureOrder, "fps avg"), new(Loc.Get("Detail_Fig_FpsAvg"), a.Groups[1].Value + " FPS", null));
        if (FpsLowest().Match(part) is { Success: true } l) return (Array.IndexOf(FigureOrder, "fps low"), new(Loc.Get("Detail_Fig_FpsLow"), l.Groups[1].Value + " FPS", null));
        return null;
    }

    private static string SceneWork(Match m)
    {
        var parts = new List<string> { Loc.Format("Detail_Work_Tri", G(m, 2), G(m, 3), G(m, 4)), G(m, 5).Length > 0 ? Loc.Format("Detail_Work_RtShadows", G(m, 5)) : Loc.Format("Detail_Work_Shadows", G(m, 6)) };
        if (G(m, 7).Length > 0) parts.Add(Loc.Format("Detail_Work_Msaa", G(m, 7)));
        if (G(m, 8).Length > 0) parts.Add(Loc.Format("Detail_Work_Pool", G(m, 8)));
        return string.Join(Loc.Get("Detail_ListComma"), parts);
    }

    public static IReadOnlyList<DetailLine> Lines(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return [];
        string all = detail.Trim();
        foreach (var rule in Whole.Value) if (rule.Pattern.Match(all) is { Success: true } m) return [.. rule.Lines(m)];
        return [.. Split(all).SelectMany(Line)];
    }

    /// <summary>The parts of a detail: cut at "; ", but not inside brackets ("ran on 30 of 32 logical processors (P-cores 16/16 threads; …)").</summary>
    private static IEnumerable<string> Split(string detail)
    {
        int depth = 0, from = 0;
        for (int i = 0; i < detail.Length; i++)
        {
            if (detail[i] == '(') depth++; else if (detail[i] == ')') depth = Math.Max(0, depth - 1);
            else if (depth == 0 && detail[i] == ';' && i + 1 < detail.Length && detail[i + 1] == ' ') { if (detail[from..i].Trim() is { Length: > 0 } p) yield return p; from = i + 2; }
        }
        if (detail[from..].Trim().TrimEnd(';').Trim() is { Length: > 0 } last) yield return last;
    }

    private static IEnumerable<DetailLine> Line(string part)
    {
        // The power test runs a processor test and a graphics test at once and reports each half under its own name.
        if (Half().Match(part) is { Success: true } half)
        {
            yield return new(Loc.Format("Detail_Half", Loc.Get(half.Groups[1].Value == "CPU" ? "Detail_Half_Cpu" : "Detail_Half_Gpu"), Loc.Get("Test_Outcome_" + half.Groups[2].Value)), false);
            foreach (var l in Line(half.Groups[3].Value)) yield return l;
            yield break;
        }
        if (NvmeDuring().Match(part) is { Success: true } nvme)
        {
            yield return new(Loc.Get("Detail_Log_NvmeDuring"), false);
            yield return new(nvme.Groups[1].Value, true);
            yield break;
        }
        if (Known(part) is { } known) { yield return known; yield break; }
        foreach (var rule in Parts.Value) if (rule.Pattern.Match(part) is { Success: true } m) { foreach (var l in rule.Lines(m)) yield return l; yield break; }
        yield return new(part, true);
    }

    /// <summary>The parts every executor shares: a measured sensor, a count, the graphics load's own lines.</summary>
    private static DetailLine? Known(string part)
    {
        if (Stat().Match(part) is { Success: true } s && Labels.TryGetValue(s.Groups[1].Value, out var label))
        {
            string unit = s.Groups[3].Value.Trim();
            return new(s.Groups[4].Success ? Loc.Format("Detail_Stat_Max", Loc.Get(label), s.Groups[2].Value, s.Groups[4].Value, unit, s.Groups[5].Value)
                : Loc.Format("Detail_Stat", Loc.Get(label), s.Groups[2].Value, unit, s.Groups[5].Value), false);
        }
        if (Count().Match(part) is { Success: true } c && Counts.TryGetValue(c.Groups[1].Value, out var count)) return new(Loc.Format(count, c.Groups[2].Value), false);
        if (GpuStress().Match(part) is { Success: true } g) return new(Loc.Format("Detail_GpuStress", Loc.Get("Detail_Profile_" + g.Groups[1].Value), g.Groups[2].Value), false);
        if (Verified().Match(part) is { Success: true } v) return new(Loc.Format("Detail_GpuVerified", v.Groups[1].Value, v.Groups[2].Value), false);
        if (FrameRate().Match(part) is { Success: true } f) return new(Loc.Format("Detail_FrameRate", f.Groups[1].Value), false);
        if (NetMetric().Match(part) is { Success: true } n && NetName(n.Groups[1].Value) is { } name) return new(Loc.Format("Detail_Net_Metric", name, n.Groups[2].Value, n.Groups[3].Value), false);
        return null;
    }

    /// <summary>"P-cores 16/16 threads; E-cores 14/16 threads; groups 0: 32/32" in the user's words.</summary>
    private static string Coverage(string inner) => string.Join(Loc.Get("Detail_ListComma"), inner.Split("; ").Select(p =>
        CoverageCores().Match(p) is { Success: true } m ? Loc.Format(m.Groups[1].Value == "P" ? "Detail_Cpu_PCores" : "Detail_Cpu_ECores", m.Groups[2].Value, m.Groups[3].Value)
        : p.StartsWith("groups ", StringComparison.Ordinal) ? Loc.Format("Detail_Cpu_Groups", p[7..]) : p));

    /// <summary>"60 s and 60 s" as the user's language says it.</summary>
    private static string Seconds(string held) => string.Join(Loc.Get("Detail_And"), held.Split(" and ").Select(h => Loc.Format("Detail_SecondsOf", h.Replace(" s", "", StringComparison.Ordinal))));

    /// <summary>A latency summary ("mean 0.050 ms, P50 0.040, …, max 1.200 ms (n=1000)", "median 0.21 ms, P95 0.30 ms (n=40)"): its three English words.</summary>
    private static string Latency(string text) => text == "no requests" || text == "not measured" ? Loc.Get("Detail_NotMeasured")
        : Samples().Replace(text.Replace("mean ", Loc.Get("Detail_Word_Mean") + " ", StringComparison.Ordinal).Replace("median ", Loc.Get("Detail_Word_Median") + " ", StringComparison.Ordinal)
            .Replace("max ", Loc.Get("Detail_Word_Max") + " ", StringComparison.Ordinal), m => Loc.Format("Detail_Samples", m.Groups[1].Value));

    /// <summary>", wear 3%, 35 °C (max 50 °C), power-on 1200 h, uncorrected read/write errors 0/0" after a drive's name and status.</summary>
    private static string DriveFacts(string tail) => tail.Replace(", ", Loc.Get("Detail_ListComma"), StringComparison.Ordinal).Replace("wear ", Loc.Get("Detail_Word_Wear") + " ", StringComparison.Ordinal)
        .Replace("(max ", "(" + Loc.Get("Detail_Word_Max") + " ", StringComparison.Ordinal).Replace("power-on ", Loc.Get("Detail_Word_PowerOn") + " ", StringComparison.Ordinal)
        .Replace("uncorrected read/write errors ", Loc.Get("Detail_Word_Uncorrected") + " ", StringComparison.Ordinal);

    /// <summary>An internet-speed number's name as the benchmark shows it ("score_gaming" is the metric Bench_Net_Score_Gaming), or null when there is none.</summary>
    private static string? NetName(string lower)
    {
        string key = "Bench_Net_" + string.Join("_", lower.Split('_').Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..]));
        return Loc.Get(key) is var name && name != key ? name : null;
    }

    [GeneratedRegex(@"^(?:measured )?([A-Za-z ]+?) avg ([\d.]+)(%|°C| W| MB| MHz) (?:max ([\d.]+)(?:%|°C| W| MB| MHz) )?\(n=(\d+)\)$")] private static partial Regex Stat();
    [GeneratedRegex(@"^([a-z]+(?: [a-z]+)?)=([\d,]+)$")] private static partial Regex Count();
    [GeneratedRegex(@"^GPU (Steady|Variable|Pulse) compute stress on (.+)$")] private static partial Regex GpuStress();
    [GeneratedRegex(@"^verified ([\d,]+) of ([\d,]+) thread results through chained dispatches \(a sample, not every thread\)$")] private static partial Regex Verified();
    [GeneratedRegex(@"^([\d,.]+) frame/s$")] private static partial Regex FrameRate();
    [GeneratedRegex(@"^([\d,.]+) FPS average$")] private static partial Regex FpsAverage();
    [GeneratedRegex(@"^([\d,.]+) FPS lowest half-second$")] private static partial Regex FpsLowest();
    [GeneratedRegex(@"^(CPU|GPU) \[(\w+)\]: (.+)$")] private static partial Regex Half();
    [GeneratedRegex(@"^NVMe health log during the test: (.+)$")] private static partial Regex NvmeDuring();
    [GeneratedRegex(@"^([a-z_]+) ([\d,.]+) ?(\S*)$")] private static partial Regex NetMetric();
    [GeneratedRegex(@"^(P|E)-cores (\d+)/(\d+) threads$")] private static partial Regex CoverageCores();
    [GeneratedRegex(@"\(n=(\d+)\)")] private static partial Regex Samples();
}
