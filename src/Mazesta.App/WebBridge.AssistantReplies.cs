using System.Globalization; using System.Text; using System.Text.Json;
using Mazesta.Core.Ai; using Mazesta.Core.Hardware; using Mazesta.Desktop.Localization;
namespace Mazesta.App;

/// <summary>
/// The answers the app writes itself, from what its tools read: a part of this computer, a reading now, a program's level, a report, the DNS
/// test. These have one right answer, and a small model got them wrong in Persian (it called Vantage "filming", read GFLOPS as a number
/// word), so the model is left out of them; it still sees them in the history for the questions that follow.
/// </summary>
internal static class AssistantReplies
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string? S(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static string T(JsonElement e, string name) => S(e, name) ?? "—";
    private static double? D(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    private static IEnumerable<JsonElement> A(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];
    private static string N(double v) => v.ToString(v % 1 == 0 ? "0" : "0.#", Inv);
    private static string Gb(double? v) => v is { } x ? N(x) + " GB" : "—";

    /// <summary>The drivers as the drivers page found them: the card's driver against NVIDIA's newest of each line and the suggested line, and the
    /// devices without a working driver. Installing is the page's (a button and a confirmation).</summary>
    public static string Drivers(string json)
    {
        using var d = JsonDocument.Parse(json); var r = d.RootElement; var lines = new List<string>();
        if (S(r, "error") is { } err) return Loc.Format("Assist_FileFailed", err);
        foreach (var g in A(r, "gpus")) lines.Add(Loc.Format("Assist_Drv_Gpu", T(g, "name"), T(g, "version"), S(g, "date") ?? "—"));
        if (r.TryGetProperty("nvidia", out var nv) && nv.ValueKind == JsonValueKind.Object)
        {
            if (S(nv, "error") is { } ne) lines.Add(ne);
            else
            {
                string? Line(string key, string newerKey, string name) => nv.TryGetProperty(key, out var x) && x.ValueKind == JsonValueKind.Object
                    ? Loc.Format(nv.TryGetProperty(newerKey, out var nw) && nw.ValueKind == JsonValueKind.True ? "Assist_Drv_Newer" : "Assist_Drv_Same", name, T(x, "version"), S(x, "date") ?? "—") : null;
                bool geforce = nv.TryGetProperty("geforce", out var gf) && gf.ValueKind == JsonValueKind.True;
                if (Line("gameReady", "newerGameReady", Loc.Get(geforce ? "Drivers_Nv_GameReady" : "Drivers_Nv_Pro")) is { } a) lines.Add(a);
                if (Line("studio", "newerStudio", Loc.Get("Drivers_Nv_Studio")) is { } b) lines.Add(b);
                if (geforce && r.TryGetProperty("advice", out var ad) && ad.ValueKind == JsonValueKind.Object)
                {
                    var creative = A(ad, "creative").Select(x => x.GetString()).Take(4).ToList(); var games = A(ad, "games").Select(x => x.GetString()).Take(4).ToList();
                    lines.Add(creative.Count > 0 && games.Count > 0 ? Loc.Format("Drivers_Advice_Both", string.Join("، ", creative), string.Join("، ", games))
                        : creative.Count > 0 ? Loc.Format("Drivers_Advice_Studio", string.Join("، ", creative)) : games.Count > 0 ? Loc.Format("Drivers_Advice_Games", string.Join("، ", games)) : Loc.Get("Drivers_Advice_None"));
                }
            }
        }
        if (r.TryGetProperty("board", out var bd) && bd.ValueKind == JsonValueKind.Object)
            foreach (var i in A(bd, "items").Where(i => i.TryGetProperty("newer", out var n) && n.ValueKind == JsonValueKind.True))
                lines.Add(Loc.Format("Assist_Drv_Board", Loc.Get($"Drivers_Part_{T(i, "part")}"), Loc.Get($"Drivers_Src_{T(i, "source")}"), T(i, "version"), S(i, "installed") ?? "—"));
        int problems = A(r, "problems").Count();
        lines.Add(problems == 0 ? Loc.Get("Drivers_Problems_None") : Loc.Format("Assist_Drv_Problems", problems, string.Join("، ", A(r, "problems").Take(4).Select(p => T(p, "name")))));
        lines.Add(Loc.Get("Assist_Drv_Page"));
        return string.Join("\n", lines);
    }

    public static string Specs(string part, string json)
    {
        using var d = JsonDocument.Parse(json); var r = d.RootElement; var lines = new List<string>();
        if (r.TryGetProperty("cpu", out var cpu) && cpu.ValueKind == JsonValueKind.Object && S(cpu, "name") is { } cn)
            lines.Add(Loc.Format("Assist_Spec_Cpu", cn, D(cpu, "cores") is { } c ? N(c) : "—", D(cpu, "threads") is { } t ? N(t) : "—"));
        if (D(r, "ramGb") is { } ram)
        {
            var mods = A(r, "ramModules").Select(m => string.Join(" · ", new[] { D(m, "gb") is { } g ? Gb(g) : null, D(m, "speedMts") is { } sp ? N(sp) + " MT/s" : null, S(m, "maker"), S(m, "part") }.Where(x => !string.IsNullOrWhiteSpace(x)))).ToList();
            lines.Add(Loc.Format("Assist_Spec_Ram", Gb(ram)) + (mods.Count > 0 ? " " + Loc.Format("Assist_Spec_Modules", mods.Count, string.Join("، ", mods)) : ""));
        }
        foreach (var g in A(r, "gpus"))
            if (S(g, "name") is { } gn)
                lines.Add(part == "vram" && D(g, "vramGb") is null ? "" : Loc.Format("Assist_Spec_Gpu", gn) + (D(g, "vramGb") is { } v ? Loc.Format("Assist_Spec_Vram", Gb(v)) : ""));
        if (S(r, "os") is { } os) lines.Add(Loc.Format("Assist_Spec_Os", os));
        if (S(r, "board") is { } b) lines.Add(Loc.Format("Assist_Spec_Board", b, S(r, "bios") ?? "—"));
        foreach (var dr in A(r, "drives")) if (S(dr, "name") is { } dn) lines.Add(Loc.Format("Assist_Spec_Drive", dn, Gb(D(dr, "sizeGb")), S(dr, "health") ?? "—"));
        lines.RemoveAll(string.IsNullOrEmpty);
        return lines.Count == 0 ? Loc.Get("Assist_Spec_None") : string.Join("\n", lines);
    }

    /// <param name="part">The part asked about (cpu, gpu, memory, storage, network), or null for all.</param>
    public static string Sensors(string? kind, string? part, string json)
    {
        using var d = JsonDocument.Parse(json); var r = d.RootElement;
        if (S(r, "error") is { } e) return Loc.Format("Assist_Sensors_None", e);
        var lines = new List<string>();
        string? want = part switch { "cpu" => "Cpu", "gpu" => "Gpu", "memory" => "Memory", "storage" => "Storage", "network" => "Network", _ => null };
        foreach (var dev in A(r, "devices").Where(x => want is null || S(x, "part") == want))
        {
            // The few that matter most: the hottest (or highest) three of each part.
            foreach (var s in A(dev, "sensors").OrderByDescending(x => D(x, "value") ?? 0).Take(kind is null ? 2 : 3))
            {
                var unit = Enum.TryParse<Unit>(S(s, "unit"), out var u) ? u : Unit.None;
                lines.Add($"- {S(dev, "device")} · {S(s, "name")}: {Units.FormatWithSymbol(D(s, "value") ?? 0, unit)}");
            }
        }
        if (lines.Count == 0) return Loc.Get("Assist_Sensors_Empty");
        string head = r.TryGetProperty("stale", out var st) && st.ValueKind == JsonValueKind.True
            ? Loc.Format("Assist_Sensors_Stale", Loc.Get("Assist_Kind_" + (kind ?? "All")), N(D(r, "secondsAgo") ?? 0))
            : Loc.Format("Assist_Sensors", Loc.Get("Assist_Kind_" + (kind ?? "All")));
        return head + "\n" + string.Join("\n", lines);
    }

    public static string Software(string json, bool one)
    {
        using var d = JsonDocument.Parse(json); var r = d.RootElement;
        if (S(r, "error") is { } e) return e;
        var programs = A(r, "programs").ToList();
        if (one && programs.Count == 1)
        {
            var p = programs[0]; var sb = new StringBuilder();
            string level = T(p, "level");
            sb.Append(Loc.Format(level == "Below" ? "Assist_Soft_Below" : level == "Meets" ? "Assist_Soft_Meets" : "Assist_Soft_One", T(p, "name"), T(p, "levelName")));
            if (level != "Meets" && S(p, "suits") is { } suits) sb.Append(' ').Append(Loc.Format("Assist_Soft_Suits", suits));
            if (S(p, "note") is { } note) sb.Append(' ').Append(note);
            var missing = A(p, "missingForNext").Select(x => x.GetString()).ToList();
            if (missing.Count > 0) sb.Append('\n').Append(Loc.Format(S(p, "level") == "Below" ? "Assist_Soft_Lacks" : "Assist_Soft_Next", T(p, "nextLevel"), string.Join("؛ ", missing)));
            var open = A(p, "notChecked").Select(x => x.GetString()).ToList();
            if (open.Count > 0) sb.Append('\n').Append(Loc.Get("Apps_Unchecked")).Append(' ').Append(string.Join("، ", open));
            sb.Append('\n').Append(Loc.Get("Assist_Soft_More"));
            return sb.ToString();
        }
        return Loc.Get("Assist_Soft_List") + "\n" + string.Join("\n", programs.Select(p => $"- {S(p, "name")}: {S(p, "levelName")}")) + "\n" + Loc.Get("Assist_Soft_More");
    }

    /// <param name="part">For the temperatures: the part asked about (cpu, gpu, memory, storage), or null for the hottest of all.</param>
    public static string Report(string listJson, string reportJson, bool temperatures, string? part = null)
    {
        using var d = JsonDocument.Parse(reportJson); var r = d.RootElement;
        if (S(r, "error") is { } e) return e;
        var temps = A(r, "highestTemperatures").Where(t => (part is null || S(t, "kind") == part) && D(t, "maxC") is not null).Take(3)
            .Select(t => $"{S(t, "part")} ({S(t, "sensor")}): {N(D(t, "maxC")!.Value)} °C").ToList();
        string head = Loc.Format(D(r, "index") is > 0 ? "Assist_Report_HeadEarlier" : "Assist_Report_Head", T(r, "createdAt"), Loc.Get("Assist_ReportKind_" + S(r, "kind")), D(r, "minutes") is { } m ? N(m) : "—");
        if (temperatures) return head + "\n" + (temps.Count == 0 ? Loc.Get(part is null ? "Assist_Report_NoTemps" : "Assist_Report_NoPartTemps") : Loc.Get("Assist_Report_Temps") + "\n" + string.Join("\n", temps.Select(x => "- " + x)));
        var lines = new List<string> { head };
        var tests = A(r, "tests").ToList();
        if (tests.Count > 0)
        {
            int passed = tests.Count(t => S(t, "outcome") == "Passed");
            lines.Add(Loc.Format("Assist_Report_Tests", S(r, "verdict") is { } v && Enum.TryParse<Reporting.ReportVerdict>(v, out _) ? Loc.Get("Reports_Verdict_" + v) : "—", passed, tests.Count));
            foreach (var t in tests.Where(t => S(t, "outcome") != "Passed")) lines.Add($"- {S(t, "name")}: {Loc.Get("Test_Outcome_" + S(t, "outcome"))}");
        }
        foreach (var b in A(r, "benchmarks"))
            lines.Add($"- {S(b, "name")}: {A(b, "results").Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : null).FirstOrDefault(x => !string.IsNullOrEmpty(x)) ?? Loc.Get("Assist_Report_NoNumber")}");
        if (temps.Count > 0) lines.Add(Loc.Get("Assist_Report_Temps") + " " + string.Join("، ", temps));
        var findings = A(r, "findings").Where(f => S(f, "level") is "Attention" or "Problem").Select(f => S(f, "title")).Distinct().ToList();
        if (findings.Count > 0) lines.Add(Loc.Get("Assist_Report_Findings") + " " + string.Join("؛ ", findings));
        if (D(r, "findingsOmitted") is { } more) lines.Add(Loc.Format("Assist_Report_FindingsMore", N(more)));
        using var l = JsonDocument.Parse(listJson);
        if (D(l.RootElement, "total") is { } total && total > 1) lines.Add(Loc.Format("Assist_Report_More", N(total)));
        return string.Join("\n", lines);
    }

    /// <summary>A test run as it ended: each test's outcome by its own name, then for the processor and the graphics card the highest temperature,
    /// whether the part was fully used, and the diagnosis' findings. A temperature without a finding on heat is given as a number, with no verdict.</summary>
    public static string Tests(string json)
    {
        using var d = JsonDocument.Parse(json); var r = d.RootElement;
        if (S(r, "error") is { } e) return Loc.Format("Assist_Tests_NotStarted", e);
        if (!(r.TryGetProperty("started", out var st) && st.ValueKind == JsonValueKind.True)) return S(r, "reason") is { } why && !why.Contains("declined", StringComparison.Ordinal) ? Loc.Format("Assist_Tests_NotStarted", why) : Loc.Get("Assist_Tests_Declined");
        var lines = new List<string> { Loc.Get("Assist_Tests_Head") };
        foreach (var t in A(r, "results"))
            lines.Add($"- {S(t, "name")}: {Loc.Get("Test_Outcome_" + (S(t, "outcome") ?? "NotRun"))}" + (D(t, "errors") is { } n ? " (" + Loc.Format("Test_Errors_Format", (long)n) + ")" : ""));
        foreach (var p in A(r, "judgment"))
        {
            string part = Loc.Get(S(p, "part") == "gpu" ? "Nav_Gpu" : "Nav_Cpu");
            lines.Add("");
            lines.Add(S(p, "device") is { } dev ? $"{part} ({dev}):" : part + ":");
            lines.Add("- " + (D(p, "highestTempC") is { } temp ? Loc.Format("Assist_Tests_Temp", N(temp)) : Loc.Get("Assist_Tests_NoTemp")));
            bool? full = p.TryGetProperty("usedFullPower", out var f) && f.ValueKind is JsonValueKind.True or JsonValueKind.False ? f.GetBoolean() : null;
            lines.Add("- " + (full is null ? Loc.Get("Assist_Tests_NoLoad") : Loc.Format(full.Value ? "Assist_Tests_Full" : "Assist_Tests_NotFull", N(D(p, "medianLoadPercent") ?? 0))));
            foreach (var x in A(p, "findings")) lines.Add($"- {S(x, "levelName")}: {S(x, "title")}");
            if (D(p, "highestTempC") is not null && !(p.TryGetProperty("heatJudged", out var h) && h.ValueKind == JsonValueKind.True)) lines.Add("- " + Loc.Get("Assist_Tests_NoHeatVerdict"));
        }
        return string.Join("\n", lines);
    }

    /// <summary>The smart diagnosis as it ended: the tests that failed, how many problems and things needing attention its findings hold, those
    /// findings (problems first), and each test's own outcome. A test that did not finish is named as that, never as a pass.</summary>
    public static string Checkup(string json)
    {
        using var d = JsonDocument.Parse(json); var r = d.RootElement;
        if (S(r, "error") is { } e) return Loc.Format("Assist_Tests_NotStarted", e);
        if (!(r.TryGetProperty("started", out var st) && st.ValueKind == JsonValueKind.True)) return S(r, "reason") is { } why && !why.Contains("declined", StringComparison.Ordinal) ? Loc.Format("Assist_Tests_NotStarted", why) : Loc.Get("Assist_Checkup_Declined");
        var lines = new List<string> { Loc.Get("Assist_Checkup_Head") };
        var findings = new List<JsonElement>();
        if (r.TryGetProperty("checkup", out var cu) && cu.ValueKind == JsonValueKind.Object)
        {
            findings.AddRange(A(cu, "setup"));
            foreach (var run in A(cu, "runs")) findings.AddRange(A(run, "findings"));
        }
        // The same finding can come from the setup and from a test of this session: said once.
        var distinct = findings.GroupBy(f => (S(f, "title"), S(f, "subject"))).Select(g => g.First()).ToList();
        var problems = distinct.Where(f => S(f, "level") == "Problem").ToList(); var attention = distinct.Where(f => S(f, "level") == "Attention").ToList();
        var results = A(r, "results").ToList();
        var failed = results.Where(x => S(x, "outcome") == "Failed").ToList(); var passed = results.Where(x => S(x, "outcome") == "Passed").ToList();
        var open = results.Where(x => S(x, "outcome") is not ("Passed" or "Failed")).ToList();
        if (failed.Count > 0) lines.Add(Loc.Format("Assist_Checkup_Failed", string.Join("، ", failed.Select(x => S(x, "test")))));
        // "Nothing found" is only said when tests it rests on really passed, and none failed.
        if (problems.Count + attention.Count == 0) { if (passed.Count > 0 && failed.Count == 0) lines.Add(Loc.Get("Assist_Checkup_Clean")); }
        else
        {
            lines.Add(Loc.Format("Assist_Checkup_Counts", problems.Count, attention.Count));
            foreach (var f in problems.Concat(attention).Take(8))
                lines.Add($"- {S(f, "levelName")}: {S(f, "title")}" + (S(f, "subject") is { Length: > 0 } sub ? $" ({sub})" : "") + (S(f, "text") is { Length: > 0 } text ? " — " + (text.Length > 200 ? text[..200] + "…" : text) : ""));
        }
        if (results.Count > 0)
        {
            lines.Add(Loc.Get("Assist_Checkup_Numbers"));
            foreach (var x in results) lines.Add($"- {S(x, "test")}: {S(x, "outcomeText")}");
        }
        if (open.Count > 0) lines.Add(Loc.Format("Assist_Checkup_NotDone", string.Join("، ", open.Select(x => S(x, "test")))));
        lines.Add(Loc.Get("Assist_Checkup_Page"));
        return string.Join("\n", lines);
    }

    /// <summary>What the newest saved report recorded about the part asked about: its tests' outcomes and benchmarks' main figures and the highest
    /// temperature then; empty when no report has a test of it (the specification stands alone, nothing is guessed).</summary>
    public static string PartTests(string json)
    {
        using var d = JsonDocument.Parse(json); var r = d.RootElement;
        if (!(r.TryGetProperty("tested", out var t) && t.ValueKind == JsonValueKind.True)) return "";
        var lines = new List<string> { Loc.Format("Assist_Part_Head", T(r, "at"), Loc.Get("Assist_ReportKind_" + S(r, "kind"))) };
        foreach (var l in A(r, "lines"))
        {
            string figures = string.Join("، ", A(l, "figures").Select(f => $"{S(f, "name")} {N(D(f, "value") ?? 0)} {S(f, "unit")}"));
            lines.Add($"- {S(l, "name")}: " + (S(l, "outcome") is { } o ? Loc.Get("Test_Outcome_" + o) : figures.Length > 0 ? figures : Loc.Get("Assist_Report_NoNumber")));
        }
        if (D(r, "maxTempC") is { } temp) lines.Add(Loc.Format("Assist_Part_Temp", N(temp)));
        return string.Join("\n", lines);
    }

    public static string Dns(string json)
    {
        using var d = JsonDocument.Parse(json); var r = d.RootElement;
        var results = A(r, "results").Select(x => $"- {S(x, "name")}: {N(D(x, "ms") ?? 0)} ms ({S(x, "answered")})");
        string inUse = string.Join("، ", A(r, "inUse").Select(x => x.GetString()));
        return (S(r, "fastest") is { } best ? Loc.Format("Assist_Dns_Best", best, N(D(r, "fastestMs") ?? 0), inUse) : Loc.Get("Assist_Dns_None")) + "\n" + string.Join("\n", results);
    }
}
