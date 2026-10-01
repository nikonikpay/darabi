namespace Mazesta.Core.Software;

/// <summary>What a program is for, as the page groups them.</summary>
public enum SoftCategory { Visualization, Rendering, Architecture, Civil, Animation, Video, Graphics }

/// <summary>A publisher's tier: the least that runs it, what it recommends, and the tier it names for the heaviest work (VR, large models, 4K).</summary>
public enum SoftTierKind { Minimum, Recommended, HighEnd }

/// <summary>What the program needs of the graphics card whatever its tier: any, a card with memory of its own, an NVIDIA card (CUDA), a card
/// with ray tracing in hardware (DXR), or DirectX Raytracing however the card provides it (some GTX cards get it from their driver, so their name
/// does not settle it: only Direct3D's own answer does).</summary>
public enum SoftGpu { Any, Dedicated, Nvidia, RayTracing, Dxr }

/// <summary>
/// One of a publisher's tiers. Only the figures it states are here; a figure it does not state is null and is not judged. <see cref="Gpu"/> and
/// <see cref="Cpu"/> are its own words (example cards, PassMark thresholds, processor families): shown, not compared, since the speed of two
/// cards is not something the app can read from their names. <see cref="ScaleKey"/> names the work the tier suits in the publisher's terms
/// where it gives them (model size, building type, video resolution), else the category's general wording. <see cref="RamPerVram"/> is a
/// publisher's rule tying the two memories (Chaos: system RAM at least the card's memory, twice it recommended). <see cref="LabelKey"/> names a
/// tier where a publisher gives several of one kind (Archicad's three recommended configurations by building size).
/// </summary>
public sealed record SoftTier(SoftTierKind Kind, double? RamGb = null, double? VramGb = null, int? Cores = null, bool RayTracing = false, string? Gpu = null, string? Cpu = null,
    string? ScaleKey = null, double? RamPerVram = null, string? LabelKey = null);

/// <param name="Source">Where the tiers were read (checked on 2026-10-01).</param>
/// <param name="Icon">An image under <c>img/apps/</c> (the publisher's own site icon), or null for a lettered tile of <see cref="Mono"/> in <see cref="Color"/>.</param>
/// <param name="Words">What people call it, Persian and English, for the assistant to recognise it (already in <see cref="AppGuide.Normalize"/> form or normalised on use).</param>
/// <param name="NotSupported">Cards the publisher names as not supported whatever their memory (D5: GTX 1050 and 1650).</param>
public sealed record SoftApp(string Id, string Name, string Vendor, SoftCategory Category, SoftGpu Gpu, IReadOnlyList<SoftTier> Tiers, string Source,
    string? Icon, string Mono, string Color, string[] Words, string PurposeKey, string? NoteKey = null, string[]? NotSupported = null);

/// <summary>The parts of this computer the tiers are judged on; null where it could not be read. <see cref="Dx12"/> and <see cref="Dxr"/> are what
/// Direct3D 12 says of the card (a hardware device, ray tracing in hardware), <see cref="Avx2"/> and <see cref="Sse42"/> what this processor runs.</summary>
public sealed record SoftMachine(string? CpuName, int? Cores, int? Threads, long? RamBytes, string? GpuName, long? VramBytes,
    bool? Dx12 = null, bool? Dxr = null, bool? Avx2 = null, bool? Sse42 = null);

/// <summary>A tier's figure this computer does not reach: what (Ram, Vram, RamVsVram, Cores, RayTracing, Dxr, Nvidia, Dedicated, Unsupported, Dx12,
/// Avx2, Sse42), the tier's figure and this computer's.</summary>
public sealed record SoftShort(string What, double? Need, double? Have);

/// <summary>The highest tier this computer meets (null: not even the minimum), what keeps it from the next one, and what the tiers met ask that
/// could not be checked (Cores not read, CpuSpeed and GpuSpeed against the publisher's example parts, Api a graphics interface the app does not
/// test, Avx2 or Sse42 unread): the level holds for what was checked, and these are said beside it, also when even the first tier is not met.
/// <see cref="Reached"/> and <see cref="NextTier"/> are the tiers themselves (a publisher may give several of one kind).</summary>
public sealed record SoftVerdict(SoftTierKind? Level, IReadOnlyList<SoftShort> Missing, SoftTierKind? Next, IReadOnlyList<string>? Unchecked = null, SoftTier? Reached = null, SoftTier? NextTier = null);

public static class SoftwareCatalog
{
    private static SoftTier Min(double? ram = null, double? vram = null, int? cores = null, bool rt = false, string? gpu = null, string? cpu = null, string? scale = null, double? perVram = null, string? label = null) => new(SoftTierKind.Minimum, ram, vram, cores, rt, gpu, cpu, scale, perVram, label);
    private static SoftTier Rec(double? ram = null, double? vram = null, int? cores = null, bool rt = false, string? gpu = null, string? cpu = null, string? scale = null, double? perVram = null, string? label = null) => new(SoftTierKind.Recommended, ram, vram, cores, rt, gpu, cpu, scale, perVram, label);
    private static SoftTier High(double? ram = null, double? vram = null, int? cores = null, bool rt = false, string? gpu = null, string? cpu = null, string? scale = null, double? perVram = null, string? label = null) => new(SoftTierKind.HighEnd, ram, vram, cores, rt, gpu, cpu, scale, perVram, label);

    /// <summary>
    /// The programs a shop is asked about most, grouped by what they do. Every figure is the publisher's (the documentation named in
    /// <see cref="SoftApp.Source"/>); where a publisher gives only one set, only that tier is here.
    /// </summary>
    public static IReadOnlyList<SoftApp> Apps { get; } =
    [
        // ——— Real-time visualisation: the graphics card does the work ———
        new("lumion", "Lumion Pro 2026", "Lumion", SoftCategory.Visualization, SoftGpu.Dedicated,
            [Min(16, 6, gpu: "GTX 1060 · RX 580 · G3DMark 8,000+", cpu: "PassMark single-thread 2,200+"),
             Rec(32, 10, gpu: "RTX 3060 · RX 6700 XT · G3DMark 14,000+", cpu: "PassMark single-thread 2,600+"),
             High(64, 16, gpu: "RTX 3090 · RX 6800 XT · G3DMark 22,000+", cpu: "PassMark single-thread 3,000+")],
            "lumion.com/requirements", "lumion.png", "Lu", "#1f4e8c", ["lumion", "لومیون", "لومین", "لیومیون"], "Soft_Purpose_Lumion"),
        new("twinmotion", "Twinmotion 2025", "Epic Games", SoftCategory.Visualization, SoftGpu.Dedicated,
            [Min(16, 6, gpu: "G3DMark 10,000+", cpu: "PassMark single-thread 2,000+", scale: "Soft_Scale_Twinmotion_Min"),
             High(64, 12, gpu: "G3DMark 20,000+", cpu: "PassMark single-thread 2,500+", scale: "Soft_Scale_Twinmotion_Rec")],
            "dev.epicgames.com (Twinmotion hardware and software specifications: minimum and high-end)", "twinmotion.png", "Tm", "#0f7a5a", ["twinmotion", "توین موشن", "تویین موشن", "توینموشن"], "Soft_Purpose_Twinmotion"),
        new("d5", "D5 Render", "Dimension 5", SoftCategory.Visualization, SoftGpu.Dxr,
            [Min(null, 4, gpu: "GTX 1060 · RX 6400 · Arc A3 (DirectX Raytracing)"),
             Rec(32, 8, gpu: "RTX 3060 (Ti)", cpu: "Core i5-11400 · Ryzen 3 5300G"),
             High(128, 24, gpu: "RTX 3090", cpu: "Core i9-13900K · Ryzen 9 7950X")],
            "d5render.com/post/system-requirements-for-d5-render", "d5render.png", "D5", "#6a3df0", ["d5 render", "d5", "دی فایو", "دی ۵", "دی5"], "Soft_Purpose_D5", "Soft_Note_D5", ["GTX 1050", "GTX 1650"]),
        new("enscape", "Enscape", "Chaos", SoftCategory.Visualization, SoftGpu.Dedicated,
            [Min(null, 4, gpu: "GTX 900 · RX 400 · Arc A310 (Vulkan 1.1)"),
             Rec(null, 8, gpu: "RTX 3070 Ti · RX 6800"),
             High(null, 12, gpu: "RTX 4070 Ti · RX 7900 XT", scale: "Soft_Scale_Vr")],
            "docs.chaos.com (Enscape system requirements, Windows)", null, "En", "#e0303a", ["enscape", "انسکیپ", "اینسکیپ", "انسکیب"], "Soft_Purpose_Enscape"),
        new("vantage", "Chaos Vantage", "Chaos", SoftCategory.Visualization, SoftGpu.RayTracing,
            [Min(8, gpu: "NVIDIA RTX 20+ · AMD RX 6000+ · Intel Arc (DXR)", cpu: "AVX2", perVram: 1), Rec(8, perVram: 2)],
            "docs.chaos.com (Vantage system requirements)", null, "Va", "#e0303a", ["vantage", "chaos vantage", "ونتیج", "ونتج", "وانتیج", "ونتیژ", "وَنتیج"], "Soft_Purpose_Vantage", "Soft_Note_Vantage"),
        new("unreal", "Unreal Engine 5", "Epic Games", SoftCategory.Visualization, SoftGpu.Dedicated,
            [Rec(32, 8, 4, cpu: "Quad-core 2.5 GHz+", gpu: "DirectX 12")],
            "dev.epicgames.com (hardware and software specifications)", "unrealengine.png", "UE", "#202020", ["unreal", "unreal engine", "آنریل", "انریل", "ue5"], "Soft_Purpose_Unreal"),

        // ——— Offline renderers ———
        new("vray", "V-Ray 7 (CPU)", "Chaos", SoftCategory.Rendering, SoftGpu.Any,
            [Min(8, cpu: "AVX2")],
            "docs.chaos.com (V-Ray system requirements)", null, "V", "#e0303a", ["v-ray", "vray", "وی ری", "ویری", "وی-ری"], "Soft_Purpose_Vray"),
        new("vraygpu", "V-Ray GPU", "Chaos", SoftCategory.Rendering, SoftGpu.Dedicated,
            [Min(8, gpu: "NVIDIA Maxwell+ (CUDA, RTX) · AMD RDNA 2+ (Chaos's listed models)", cpu: "AVX2"), Rec(32, cpu: "AVX2", perVram: 2)],
            "support.chaos.com (V-Ray GPU minimal and recommended system requirements)", null, "VG", "#e0303a", ["v-ray gpu", "vray gpu", "ویری جی پی یو", "ویری gpu"], "Soft_Purpose_VrayGpu", "Soft_Note_Vram"),
        new("corona", "Corona Renderer 13", "Chaos", SoftCategory.Rendering, SoftGpu.Any,
            [Min(cpu: "SSE4.2"), Rec(32, scale: "Soft_Scale_Corona_Rec"), High(64, scale: "Soft_Scale_Corona_High")],
            "support.chaos.com (What are the system requirements of Corona?)", null, "Co", "#e0303a", ["corona", "کرونا"], "Soft_Purpose_Corona"),

        // ——— Architecture and CAD ———
        new("revit", "Revit 2026", "Autodesk", SoftCategory.Architecture, SoftGpu.Dedicated,
            [Min(16, 4, gpu: "DirectX 11 · Shader Model 5", scale: "Soft_Scale_Revit_Min"), Rec(32, 4, scale: "Soft_Scale_Revit_Rec"), High(64, 4, scale: "Soft_Scale_Revit_High")],
            "autodesk.com (System requirements for Revit 2026)", null, "R", "#1d6fb8", ["revit", "رویت", "روت"], "Soft_Purpose_Revit"),
        new("archicad", "Archicad 28", "Graphisoft", SoftCategory.Architecture, SoftGpu.Dedicated,
            [Min(8, gpu: "DirectX 11", scale: "Soft_Scale_Archicad_Base"),
             Rec(16, 4, gpu: "DirectX 11", cpu: "Core i5 · Ryzen 5", scale: "Soft_Scale_Archicad_Min", label: "Soft_Tier_Archicad_Entry"),
             Rec(32, 6, gpu: "DirectX 11", cpu: "Core i7 · Ryzen 7", scale: "Soft_Scale_Archicad_Rec", label: "Soft_Tier_Archicad_Mid"),
             Rec(64, 8, gpu: "DirectX 11", cpu: "Core i9 · Ryzen 9", scale: "Soft_Scale_Archicad_High", label: "Soft_Tier_Archicad_High")],
            "graphisoft.com/system-requirements-28", "graphisoft.png", "Ar", "#1c64d8", ["archicad", "آرشیکد", "ارشیکد", "آرچیکد", "ارچیکد"], "Soft_Purpose_Archicad"),
        new("sketchup", "SketchUp 2025", "Trimble", SoftCategory.Architecture, SoftGpu.Dedicated,
            [Rec(8, 8, gpu: "OpenGL 3.1", cpu: "2 GHz+"), High(8, 32, scale: "Soft_Scale_SketchupPbr")],
            "help.sketchup.com/en/sketchup/system-requirements", "sketchup.png", "Sk", "#d64532", ["sketchup", "اسکچاپ", "اسکچ اپ", "اسکیچاپ"], "Soft_Purpose_Sketchup"),
        new("autocad", "AutoCAD 2026", "Autodesk", SoftCategory.Architecture, SoftGpu.Any,
            [Min(8, 2, gpu: "DirectX 11", scale: "Soft_Scale_Cad2d"), Rec(32, 8, gpu: "DirectX 12", scale: "Soft_Scale_Cad3d")],
            "autodesk.com (System requirements for AutoCAD 2026)", null, "A", "#c8102e", ["autocad", "اتوکد", "آتوکد", "اتو کد"], "Soft_Purpose_Autocad"),
        new("rhino", "Rhino 8", "Robert McNeel", SoftCategory.Architecture, SoftGpu.Any,
            [Rec(8, 4, gpu: "OpenGL 4.5")],
            "rhino3d.com/8/system-requirements", "rhino3d.jpg", "Rh", "#606060", ["rhino", "راینو", "راینو ۸"], "Soft_Purpose_Rhino"),

        // ——— Civil and structural ———
        new("etabs", "ETABS", "CSI", SoftCategory.Civil, SoftGpu.Any,
            [Min(16, 1), Rec(64, 4, cpu: "12th-gen Core i5/i7/i9 · Ryzen 5/7/9 (Zen 3)")],
            "csiamerica.com/products/etabs/system-requirements", "csiamerica.png", "ET", "#274b8f", ["etabs", "ایتبس", "ای تبس", "ایتبز"], "Soft_Purpose_Etabs"),
        new("civil3d", "Civil 3D 2026", "Autodesk", SoftCategory.Civil, SoftGpu.Any,
            [Min(8, 2, gpu: "DirectX 11"), Rec(32, 8, gpu: "DirectX 12")],
            "autodesk.com (System requirements for Civil 3D 2026)", null, "C3", "#0a7d74", ["civil 3d", "civil3d", "سیویل تری دی", "سیویل ۳دی", "سیویل"], "Soft_Purpose_Civil3d"),
        new("tekla", "Tekla Structures 2025", "Trimble", SoftCategory.Civil, SoftGpu.Dedicated,
            [Rec(16, gpu: "RTX 3060 / 3070 (two monitors)"), High(32, gpu: "RTX 4080 / 4090")],
            "support.tekla.com (Tekla Structures 2025 hardware recommendations)", "tekla.png", "Tk", "#0e416c", ["tekla", "تکلا"], "Soft_Purpose_Tekla"),

        // ——— 3D, animation and effects ———
        new("3dsmax", "3ds Max 2026", "Autodesk", SoftCategory.Animation, SoftGpu.Any,
            [Min(4, cpu: "SSE4.2"), Rec(8)],
            "autodesk.com (System requirements for 3ds Max 2026)", null, "3ds", "#0f8c8c", ["3ds max", "3dsmax", "3d max", "3dmax", "تری دی مکس", "تریدی مکس", "تری‌دی مکس", "۳دی مکس"], "Soft_Purpose_3dsMax", "Soft_Note_Autodesk"),
        new("maya", "Maya 2026", "Autodesk", SoftCategory.Animation, SoftGpu.Any,
            [Min(8, cpu: "SSE4.2"), Rec(16)],
            "autodesk.com (System requirements for Maya 2026)", null, "M", "#0f8c8c", ["maya", "مایا"], "Soft_Purpose_Maya", "Soft_Note_Autodesk"),
        new("blender", "Blender 4.5 LTS", "Blender Foundation", SoftCategory.Animation, SoftGpu.Any,
            [Min(8, 2, 4, gpu: "OpenGL 4.3 · Vulkan 1.3", cpu: "SSE4.2"), Rec(32, 8, 8)],
            "blender.org/download/requirements", "blender.png", "Bl", "#e87d0d", ["blender", "بلندر"], "Soft_Purpose_Blender"),
        new("c4d", "Cinema 4D 2025", "Maxon", SoftCategory.Animation, SoftGpu.Any,
            [Min(16, 4), Rec(24, 8, gpu: "Redshift GPU: NVIDIA RTX · AMD RDNA 2+")],
            "maxon.net/en/requirements/cinema-4d-2025-requirements", "maxon.png", "C4", "#011a6a", ["cinema 4d", "c4d", "سینما فوردی", "سینمافوردی", "سینما ۴دی", "سینما 4d"], "Soft_Purpose_C4d"),
        new("aftereffects", "After Effects 2025", "Adobe", SoftCategory.Animation, SoftGpu.Dedicated,
            [Min(16, 4, cpu: "Intel 6th gen · Ryzen 1000+ · AVX2", scale: "Soft_Scale_Hd"), Rec(32, 8, cpu: "Intel 11th gen (Quick Sync) · Ryzen 3000+", scale: "Soft_Scale_4k")],
            "helpx.adobe.com (After Effects 25.x system requirements)", null, "Ae", "#3b2d8f", ["after effects", "aftereffects", "افتر افکت", "افترافکت", "افتر افکتس"], "Soft_Purpose_AfterEffects"),

        // ——— Video editing ———
        new("premiere", "Premiere Pro 2025", "Adobe", SoftCategory.Video, SoftGpu.Any,
            [Min(8, 2, cpu: "Intel 6th gen · Ryzen 1000+ (AVX2)"), Rec(16, 8, cpu: "Intel 11th gen · Ryzen 3000+", scale: "Soft_Scale_Hd"), High(32, 8, scale: "Soft_Scale_4k")],
            "helpx.adobe.com/premiere-pro/system-requirements.html", null, "Pr", "#2a1466", ["premiere", "premiere pro", "پریمیر", "پرمیر", "پریمیر پرو"], "Soft_Purpose_Premiere"),
        new("resolve", "DaVinci Resolve 20", "Blackmagic Design", SoftCategory.Video, SoftGpu.Dedicated,
            [Min(16, 4, gpu: "CUDA · OpenCL · Metal"), Rec(32, 4, scale: "Soft_Scale_Fusion")],
            "blackmagicdesign.com/products/davinciresolve (system requirements)", null, "Dv", "#3c3c3c", ["davinci", "davinci resolve", "resolve", "داوینچی", "داوینچی ریزالو"], "Soft_Purpose_Resolve"),

        // ——— Graphics ———
        new("photoshop", "Photoshop 2025", "Adobe", SoftCategory.Graphics, SoftGpu.Any,
            [Min(8, 1.5, gpu: "DirectX 12 (feature level 12_0)", cpu: "AVX2 · SSE4.2"), Rec(16, 2, gpu: "DirectX 12 (feature level 12_0)", scale: "Soft_Scale_Photo4k")],
            "helpx.adobe.com (Photoshop on desktop technical requirements, 26.x)", null, "Ps", "#0b3d66", ["photoshop", "فتوشاپ", "فوتوشاپ"], "Soft_Purpose_Photoshop"),
    ];

    public static SoftApp? Find(string id) => Apps.FirstOrDefault(a => a.Id == id);

    /// <summary>Memory reads a little under its nominal size (a "16 GB" machine shows 15.8 GiB, less with a card that borrows some); publishers
    /// name the nominal size.</summary>
    /// The card's memory is read as Windows reports it (an 8 GB card shows 7.98 GiB): a small allowance, so a 1 GiB card never passes 1.5 GB.
    public const double RamSlackGb = 1, VramSlackGb = 0.25;

    public enum GpuMaker { None, Nvidia, Amd, Intel, Other }

    public static GpuMaker Maker(string? gpu) => gpu is null ? GpuMaker.None
        : Has(gpu, "NVIDIA") || Has(gpu, "GeForce") || Has(gpu, "Quadro") || Has(gpu, "Tesla") ? GpuMaker.Nvidia
        : Has(gpu, "AMD") || Has(gpu, "Radeon") ? GpuMaker.Amd : Has(gpu, "Intel") || Has(gpu, "Arc") ? GpuMaker.Intel : GpuMaker.Other;

    /// <summary>
    /// Whether the card traces rays in hardware (DirectX Raytracing), from its family, which fixes it: NVIDIA's RTX cards (GeForce RTX 20 and
    /// later, Quadro RTX, RTX A and Ada), AMD's RDNA 2 and later (Radeon RX 6000, 7000, 9000; Radeon PRO W6000, W7000), Intel Arc. Null for a
    /// name that is none of the families known either way.
    /// </summary>
    public static bool? RayTracing(string? gpu)
    {
        if (gpu is null) return null;
        if (Has(gpu, "RTX")) return true;
        if (Has(gpu, "GTX") || Has(gpu, "GeForce GT ") || Has(gpu, "Quadro")) return false;
        if (System.Text.RegularExpressions.Regex.Match(gpu, @"\bRX\s*(\d{3,4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase) is { Success: true } rx)
            return rx.Groups[1].Value.Length == 4 && rx.Groups[1].Value[0] is '6' or '7' or '9';
        if (System.Text.RegularExpressions.Regex.IsMatch(gpu, @"\bW[67]\d{3}\b")) return true;
        if (Has(gpu, "Arc")) return true;
        if (Has(gpu, "Vega") || Has(gpu, "UHD") || Has(gpu, "Iris") || Has(gpu, "HD Graphics")) return false;
        return null;
    }

    private static bool Has(string s, string part) => s.Contains(part, StringComparison.OrdinalIgnoreCase);

    /// <summary>What of one tier this computer lacks; empty when it meets it. A figure the computer's reading lacks counts as not met, except the
    /// cores and the processor's instructions (when they could not be read they are not held against it, and <see cref="Unchecked"/> says so).</summary>
    public static IReadOnlyList<SoftShort> Lacks(SoftApp app, SoftTier tier, SoftMachine pc)
    {
        var lacks = new List<SoftShort>();
        double? ram = pc.RamBytes / (double)(1L << 30), vram = pc.VramBytes is > 0 ? pc.VramBytes / (double)(1L << 30) : null;
        bool dedicated = vram is not null;
        if (app.Gpu is SoftGpu.Dedicated or SoftGpu.Nvidia or SoftGpu.RayTracing or SoftGpu.Dxr && !dedicated) lacks.Add(new("Dedicated", null, null));
        else if (app.Gpu == SoftGpu.Nvidia && Maker(pc.GpuName) != GpuMaker.Nvidia) lacks.Add(new("Nvidia", null, null));
        if (pc.GpuName is { } card && app.NotSupported?.Any(n => Has(card, n)) == true) lacks.Add(new("Unsupported", null, null));
        if (app.Gpu == SoftGpu.Dxr && dedicated && pc.Dxr == false) lacks.Add(new("Dxr", null, null));
        if ((app.Gpu == SoftGpu.RayTracing || tier.RayTracing) && dedicated && (pc.Dxr ?? RayTracing(pc.GpuName)) != true) lacks.Add(new("RayTracing", null, null));
        if (Has(tier.Gpu ?? "", "DirectX 12") && pc.Dx12 == false) lacks.Add(new("Dx12", null, null));
        if (Has(tier.Cpu ?? "", "AVX2") && pc.Avx2 == false) lacks.Add(new("Avx2", null, null));
        if (Has(tier.Cpu ?? "", "SSE4.2") && pc.Sse42 == false) lacks.Add(new("Sse42", null, null));
        if (tier.RamGb is { } r && (ram ?? 0) + RamSlackGb < r) lacks.Add(new("Ram", r, ram is null ? null : Math.Round(ram.Value, 1)));
        if (tier.VramGb is { } v && (vram ?? 0) + VramSlackGb < v) lacks.Add(new("Vram", v, vram is null ? null : Math.Round(vram.Value, 1)));
        if (tier.RamPerVram is { } k && vram is { } cardGb && (ram ?? 0) + RamSlackGb < cardGb * k) lacks.Add(new("RamVsVram", Math.Round(cardGb * k, 1), ram is null ? null : Math.Round(ram.Value, 1)));
        if (tier.Cores is { } c && pc.Cores is { } have && have < c) lacks.Add(new("Cores", c, have));
        return lacks;
    }

    /// <summary>What a tier asks that this computer's reading can not settle either way.</summary>
    public static IEnumerable<string> Unchecked(SoftTier tier, SoftMachine pc) => Unchecked(null, tier, pc);

    public static IEnumerable<string> Unchecked(SoftApp? app, SoftTier tier, SoftMachine pc)
    {
        if (tier.Cores is not null && pc.Cores is null) yield return "Cores";
        string cpu = tier.Cpu ?? "", gpu = tier.Gpu ?? "";
        if (Has(cpu, "AVX2") && pc.Avx2 is null || Has(cpu, "SSE4.2") && pc.Sse42 is null) yield return "Instructions";
        // Whatever else the publisher names of the processor (a model, a clock, a PassMark figure) is its speed, which the app does not compare.
        if (System.Text.RegularExpressions.Regex.Replace(cpu, @"AVX2|SSE4\.2|·", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim().Length > 0) yield return "CpuSpeed";
        if (System.Text.RegularExpressions.Regex.IsMatch(gpu, @"G3DMark|GTX|RTX|RX\s*\d|Arc|Radeon|GeForce|Quadro|RDNA|Maxwell", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) yield return "GpuSpeed";
        // A feature level (12_0) is more than "Direct3D 12 works", which is all the app reads.
        if (Has(gpu, "OpenGL") || Has(gpu, "Vulkan") || Has(gpu, "Shader Model") || Has(gpu, "12_0") || Has(gpu, "DirectX 11") && pc.Dx12 != true || Has(gpu, "DirectX 12") && pc.Dx12 is null
            || app?.Gpu == SoftGpu.Dxr && pc.Dxr is null) yield return "Api";
    }

    /// <summary>The name of the verdict for the page and the assistant: a program whose publisher gives one set of requirements either meets it or
    /// not ("Meets", not "Minimum": an RTX 3090 is not the least that runs Vantage).</summary>
    /// A program whose publisher gives no minimum and is not met is "BelowRec": below the publisher's recommendation, not below a minimum it never named.
    public static string LevelName(SoftApp app, SoftVerdict v) => v.Level is null ? (app.Tiers[0].Kind == SoftTierKind.Minimum ? "Below" : "BelowRec")
        : app.Tiers.Count == 1 ? "Meets" : v.Level.Value.ToString();

    /// <summary>The highest tier met (tiers are met in order: a higher tier counts only when every lower one is met too), and what the next
    /// tier needs that is missing. A program with only a recommended tier is either at it or below it.</summary>
    public static SoftVerdict Judge(SoftApp app, SoftMachine pc)
    {
        SoftTier? reached = null; var open = new List<string>();
        foreach (var tier in app.Tiers)
        {
            var lacks = Lacks(app, tier, pc);
            // What could not be checked stays beside the verdict whether or not the tier is met: a shortfall in RAM does not make the unread parts read.
            open.AddRange(Unchecked(app, tier, pc));
            if (lacks.Count > 0) return new(reached?.Kind, lacks, tier.Kind, open.Distinct().ToList(), reached, tier);
            reached = tier;
        }
        return new(reached?.Kind, [], null, open.Distinct().ToList(), reached, null);
    }
}
