namespace Mazesta.Core.Software;

/// <summary>What a program is for, as the page groups them; <see cref="Game"/> is the games page's, judged the same way on the publisher's tiers.</summary>
public enum SoftCategory { Visualization, Rendering, Architecture, Civil, Mechanical, Animation, Video, Graphics, Game }

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
/// tier where a publisher gives several of one kind (Archicad's three recommended configurations by building size). <see cref="Target"/> is a game
/// publisher's own aim for the tier (resolution, preset, frame rate) in its words, where it gives one: a target it tested, not a promise for
/// another computer, and never made up where the publisher names none.
/// </summary>
public sealed record SoftTier(SoftTierKind Kind, double? RamGb = null, double? VramGb = null, int? Cores = null, bool RayTracing = false, string? Gpu = null, string? Cpu = null,
    string? ScaleKey = null, double? RamPerVram = null, string? LabelKey = null, string? Target = null);

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
    private static SoftTier Min(double? ram = null, double? vram = null, int? cores = null, bool rt = false, string? gpu = null, string? cpu = null, string? scale = null, double? perVram = null, string? label = null, string? target = null) => new(SoftTierKind.Minimum, ram, vram, cores, rt, gpu, cpu, scale, perVram, label, target);
    private static SoftTier Rec(double? ram = null, double? vram = null, int? cores = null, bool rt = false, string? gpu = null, string? cpu = null, string? scale = null, double? perVram = null, string? label = null, string? target = null) => new(SoftTierKind.Recommended, ram, vram, cores, rt, gpu, cpu, scale, perVram, label, target);
    private static SoftTier High(double? ram = null, double? vram = null, int? cores = null, bool rt = false, string? gpu = null, string? cpu = null, string? scale = null, double? perVram = null, string? label = null, string? target = null) => new(SoftTierKind.HighEnd, ram, vram, cores, rt, gpu, cpu, scale, perVram, label, target);

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
        new("unity", "Unity 6.6 (Editor)", "Unity", SoftCategory.Visualization, SoftGpu.Any,
            [Rec(8, gpu: "DirectX 10 / 11 / 12 · Vulkan")],
            "docs.unity3d.com (System requirements for Unity 6.6: the Editor)", null, "U", "#222c37", ["unity", "یونیتی", "یونیتي"], "Soft_Purpose_Unity"),
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
        new("sap2000", "SAP2000", "CSI", SoftCategory.Civil, SoftGpu.Any,
            [Min(16), Rec(64, 0.5, gpu: "Dedicated · DirectX 11 (NVIDIA or equivalent)", cpu: "12th-gen Core i5/i7/i9 · Ryzen 5/7/9 (Zen 3)")],
            "csiamerica.com/products/sap2000/system-requirements", "csiamerica.png", "SAP", "#274b8f", ["sap2000", "sap 2000", "sap", "ساپ", "ساپ ۲۰۰۰", "ساپ2000"], "Soft_Purpose_Sap2000"),
        new("safe", "SAFE", "CSI", SoftCategory.Civil, SoftGpu.Any,
            [Min(8), Rec(32, 0.5, gpu: "Dedicated · DirectX 11 (NVIDIA or equivalent)", cpu: "12th-gen Core i5/i7/i9 · Ryzen 5/7/9 (Zen 3)")],
            "csiamerica.com/products/safe/system-requirements", "csiamerica.png", "SF", "#274b8f", ["csi safe", "safe csi", "نرم افزار سیف", "سیف csi"], "Soft_Purpose_Safe"),
        new("tekla", "Tekla Structures 2025", "Trimble", SoftCategory.Civil, SoftGpu.Dedicated,
            [Rec(16, gpu: "RTX 3060 / 3070 (two monitors)"), High(32, gpu: "RTX 4080 / 4090")],
            "support.tekla.com (Tekla Structures 2025 hardware recommendations)", "tekla.png", "Tk", "#0e416c", ["tekla", "تکلا"], "Soft_Purpose_Tekla"),

        // ——— Mechanical design ———
        new("solidworks", "SOLIDWORKS 2026", "Dassault Systèmes", SoftCategory.Mechanical, SoftGpu.Any,
            [Min(16, gpu: "Certified cards and drivers (SOLIDWORKS's list)"), Rec(32)],
            "solidworks.com/support/system-requirements", null, "SW", "#da291c", ["solidworks", "solid works", "سالیدورک", "سالید ورک", "سالیدورکس", "سالید ورکس"], "Soft_Purpose_Solidworks", "Soft_Note_Solidworks"),
        new("fusion", "Autodesk Fusion", "Autodesk", SoftCategory.Mechanical, SoftGpu.Any,
            [Min(8, 1, gpu: "DirectX 11 (Direct3D 10.1+) · Intel UHD · Radeon Vega · MX", cpu: "2 performance cores · 4 threads · 3 GHz+ turbo (Core i3 · Ryzen 3)"),
             Rec(32, 8, gpu: "Dedicated · DirectX 11 · certified: Radeon Pro WX · Arc Pro · Quadro", cpu: "8+ performance cores · 16+ threads · 3 GHz+ base (Core i7 · Ryzen 7)")],
            "autodesk.com (System requirements for Autodesk Fusion, Windows)", null, "F", "#f37321", ["fusion", "fusion 360", "fusion360", "فیوژن", "فیوژن ۳۶۰"], "Soft_Purpose_Fusion"),

        // ——— 3D, animation and effects ———
        new("3dsmax", "3ds Max 2026", "Autodesk", SoftCategory.Animation, SoftGpu.Any,
            [Min(4, cpu: "SSE4.2"), Rec(8)],
            "autodesk.com (System requirements for 3ds Max 2026)", null, "3ds", "#0f8c8c", ["3ds max", "3dsmax", "3d max", "3dmax", "تری دی مکس", "تریدی مکس", "تری‌دی مکس", "۳دی مکس"], "Soft_Purpose_3dsMax", "Soft_Note_Autodesk"),
        new("maya", "Maya 2026", "Autodesk", SoftCategory.Animation, SoftGpu.Any,
            [Min(8, cpu: "SSE4.2"), Rec(16)],
            "autodesk.com (System requirements for Maya 2026)", null, "M", "#0f8c8c", ["maya", "مایا"], "Soft_Purpose_Maya", "Soft_Note_Autodesk"),
        new("blender", "Blender 5.2 LTS", "Blender Foundation", SoftCategory.Animation, SoftGpu.Any,
            [Min(8, 2, 4, gpu: "OpenGL 4.3 · Vulkan 1.3 · GeForce 900+ · AMD GCN 4+ · Intel Kaby Lake+", cpu: "SSE4.2"), Rec(32, 8, 8)],
            "blender.org/download/requirements (Blender 5.2 LTS)", "blender.png", "Bl", "#e87d0d", ["blender", "بلندر"], "Soft_Purpose_Blender"),
        new("houdini", "Houdini 22", "SideFX", SoftCategory.Animation, SoftGpu.Any,
            [Min(16, 12, gpu: "OpenGL 4.0 · OpenCL 1.2", cpu: "AVX2"), Rec(32), High(64, scale: "Soft_Scale_Houdini_High")],
            "sidefx.com/Support/system-requirements (Houdini 22)", null, "H", "#ff4713", ["houdini", "هودینی", "هوديني"], "Soft_Purpose_Houdini", "Soft_Note_Houdini"),
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
        new("illustrator", "Illustrator 2026", "Adobe", SoftCategory.Graphics, SoftGpu.Any,
            [Min(8, cpu: "SSE4.2"), Rec(16, 4, gpu: "GPU Performance: DirectX 12 · OpenGL 4.0")],
            "helpx.adobe.com (Illustrator technical requirements, version 30.0 and later)", null, "Ai", "#ff9a00", ["illustrator", "ایلوستریتور", "ایلاستریتور", "ایلستریتور"], "Soft_Purpose_Illustrator"),
        new("lightroom", "Lightroom Classic 15", "Adobe", SoftCategory.Graphics, SoftGpu.Any,
            [Min(8, 2, gpu: "DirectX 12", cpu: "SSE4.2 · AVX2"), Rec(16, 4, gpu: "DirectX 12", scale: "Soft_Scale_Photo4k"), High(16, 8, gpu: "DirectX 12", scale: "Soft_Scale_Lightroom_Ai")],
            "helpx.adobe.com (Lightroom Classic system requirements, version 15.0 and later)", null, "Lr", "#31a8ff", ["lightroom", "lightroom classic", "لایت روم", "لایتروم"], "Soft_Purpose_Lightroom", "Soft_Note_Lightroom"),

        // ——— Games: the publishers' own tiers (their store page or support page), the same judgement as the programs. Example cards and
        // processors are shown, not compared; a VRAM figure is here only where the publisher states one, not read off a card's name. ———
        new("cyberpunk", "Cyberpunk 2077", "CD PROJEKT RED", SoftCategory.Game, SoftGpu.Any,
            [Min(12, 6, gpu: "GTX 1060 6GB · RX 580 8GB · Arc A380 · DirectX 12", cpu: "Core i7-6700 · Ryzen 5 1600", target: "1080p · Low · 30 FPS"),
             Rec(16, 8, gpu: "RTX 2060 SUPER · RX 5700 XT · Arc A770 · DirectX 12", cpu: "Core i7-12700 · Ryzen 7 7800X3D", target: "1080p · High · 60 FPS"),
             High(20, 12, gpu: "RTX 3080 · RX 7900 XTX · DirectX 12", cpu: "Core i9-12900 · Ryzen 9 7900X", target: "2160p · Ultra · 60 FPS")],
            "cyberpunk.net (Update to PC system requirements, 2023-06-11) · Steam", null, "CP", "#fcee0a", ["cyberpunk", "cyberpunk 2077", "سایبرپانک", "سایبر پانک"], "Soft_Purpose_Cyberpunk", "Soft_Note_Cyberpunk"),
        new("valorant", "VALORANT", "Riot Games", SoftCategory.Game, SoftGpu.Any,
            [Min(4, gpu: "Intel HD 4000 · Radeon R5 220 · DirectX 11", cpu: "SSE4.2 · i3-540 · Athlon 200GE", target: "30 FPS"),
             Rec(4, gpu: "GeForce GT 730 · Radeon R7 240 · DirectX 11", cpu: "SSE4.2 · i3-4150 · Ryzen 3 1200", target: "60 FPS"),
             High(4, gpu: "GTX 1050 Ti · Radeon R7 370 · Arc A310 · DirectX 11", cpu: "SSE4.2 · i5-9400F · Ryzen 5 2600X", target: "144 FPS")],
            "support.riotgames.com (VALORANT minimum and recommended PC specs)", null, "V", "#ff4655", ["valorant", "والورانت", "ولورانت", "والرانت"], "Soft_Purpose_Valorant", "Soft_Note_Valorant"),
        new("cs2", "Counter-Strike 2", "Valve", SoftCategory.Game, SoftGpu.Any,
            [Min(8, 1, gpu: "DirectX 11 · Shader Model 5.0", cpu: "4 threads · Core i5 750+")],
            "store.steampowered.com/app/730", null, "CS", "#de9b35", ["counter strike", "counter-strike", "cs2", "cs 2", "کانتر", "کانتر استرایک", "سی اس"], "Soft_Purpose_Cs2"),
        new("dota2", "Dota 2", "Valve", SoftCategory.Game, SoftGpu.Any,
            [Min(4, gpu: "GeForce 8600/9600GT · Radeon HD 2600/3600 · DirectX 11", cpu: "Dual core 2.8 GHz")],
            "store.steampowered.com/app/570", null, "D2", "#a72714", ["dota", "dota 2", "dota2", "دوتا", "دوتا ۲"], "Soft_Purpose_Dota2"),
        new("apex", "Apex Legends", "Respawn · EA", SoftCategory.Game, SoftGpu.Any,
            [Min(6, gpu: "Radeon HD 7790 · GTX 950 · DirectX 12", cpu: "Core i3 6300 · FX 4350"), Rec(8, gpu: "Radeon R9 290 · GTX 970 · DirectX 12", cpu: "Ryzen 5")],
            "store.steampowered.com/app/1172470", null, "Ap", "#da292a", ["apex", "apex legends", "اپکس", "ایپکس"], "Soft_Purpose_Apex"),
        new("pubg", "PUBG: BATTLEGROUNDS", "KRAFTON", SoftCategory.Game, SoftGpu.Any,
            [Min(8, gpu: "GTX 960 2GB · R7 370 2GB · DirectX 11", cpu: "Core i5-4430 · FX-6300"), Rec(16, gpu: "GTX 1060 3GB · RX 580 4GB · DirectX 11", cpu: "Core i5-6600K · Ryzen 5 1600")],
            "store.steampowered.com/app/578080", null, "PG", "#f2a900", ["pubg", "پابجی", "پاب جی"], "Soft_Purpose_Pubg"),
        new("bf6", "Battlefield 6", "EA", SoftCategory.Game, SoftGpu.Any,
            [Min(16, gpu: "RTX 2060 · RX 5600 XT 6GB · Arc A380 · DirectX 12", cpu: "Core i5-8400 · Ryzen 5 2600"),
             Rec(16, gpu: "RTX 3060 Ti · RX 6700 XT · Arc B580 · DirectX 12", cpu: "Core i7-10700 · Ryzen 7 3700X")],
            "store.steampowered.com/app/2807960", null, "BF", "#ff6a13", ["battlefield", "battlefield 6", "بتلفیلد", "بتل فیلد"], "Soft_Purpose_Bf6", "Soft_Note_Bf6"),
        new("r6", "Rainbow Six Siege", "Ubisoft", SoftCategory.Game, SoftGpu.Any,
            [Min(8, gpu: "GTX 1650 4GB · RX 5500 XT 4GB · Arc A380 · DirectX 12", cpu: "Ryzen 3 3100 · i3-8100"), Rec(8, gpu: "RTX 2060 6GB · RX 6600 8GB · DirectX 12", cpu: "Ryzen 5 3600 · i5-10400")],
            "store.steampowered.com/app/359550", null, "R6", "#4b6b8a", ["rainbow six", "rainbow six siege", "r6", "رینبو", "رینبو سیکس"], "Soft_Purpose_R6"),
        new("rivals", "Marvel Rivals", "NetEase", SoftCategory.Game, SoftGpu.Any,
            [Min(16, gpu: "GTX 1060 · RX 580 · Arc A380 · DirectX 12", cpu: "Core i5-6600K · Ryzen 5 1600X"), Rec(16, gpu: "RTX 2060 (Super) · RX 5700 XT · Arc A750 · DirectX 12", cpu: "Core i5-10400 · Ryzen 5 5600X")],
            "store.steampowered.com/app/2767030", null, "MR", "#e8262b", ["marvel rivals", "مارول رایوالز", "مارول"], "Soft_Purpose_Rivals"),
        new("fc26", "EA SPORTS FC 26", "EA", SoftCategory.Game, SoftGpu.Any,
            [Min(8, gpu: "RX 570 · GTX 1050 Ti · DirectX 12 (feature level 12_0)", cpu: "Ryzen 5 1600 · Core i5 6600K"), Rec(12, gpu: "RX 5600 XT · GTX 1660 · DirectX 12 (feature level 12_0)", cpu: "Ryzen 7 2700X · Core i7 6700")],
            "store.steampowered.com/app/3405690", null, "FC", "#14d27a", ["fc 26", "fc26", "ea fc", "fifa", "فیفا", "اف سی"], "Soft_Purpose_Fc26"),
        new("forza5", "Forza Horizon 5", "Playground Games", SoftCategory.Game, SoftGpu.Any,
            [Min(8, gpu: "GTX 970 · RX 470 · Arc A380 · DirectX 12", cpu: "Core i5-4460 · Ryzen 3 1200"), Rec(16, gpu: "GTX 1070 · RX 590 · Arc A750 · DirectX 12", cpu: "Core i5-8400 · Ryzen 5 1500X")],
            "store.steampowered.com/app/1551360", null, "FH", "#e8378a", ["forza", "forza horizon", "forza horizon 5", "فورزا"], "Soft_Purpose_Forza5"),
        new("gta5", "GTA V Enhanced", "Rockstar Games", SoftCategory.Game, SoftGpu.Any,
            [Min(8, 4, gpu: "GTX 1630 · RX 6400", cpu: "Core i7-4770 · FX-9590"), Rec(16, 8, gpu: "RTX 3060 · RX 6600 XT", cpu: "Core i5-9600K · Ryzen 5 3600")],
            "store.steampowered.com/app/3240220", null, "GTA", "#5aa83c", ["gta", "gta v", "gta 5", "gta5", "جی تی ای", "جی تی ای ۵"], "Soft_Purpose_Gta5", "Soft_Note_Ssd"),
        new("rdr2", "Red Dead Redemption 2", "Rockstar Games", SoftCategory.Game, SoftGpu.Any,
            [Min(8, gpu: "GTX 770 2GB · R9 280 3GB", cpu: "Core i5-2500K · FX-6300"), Rec(12, gpu: "GTX 1060 6GB · RX 480 4GB", cpu: "Core i7-4770K · Ryzen 5 1500X")],
            "store.steampowered.com/app/1174180", null, "RD", "#b5121b", ["red dead", "red dead redemption", "rdr2", "رد دد", "رددد"], "Soft_Purpose_Rdr2"),
        new("eldenring", "ELDEN RING", "FromSoftware", SoftCategory.Game, SoftGpu.Any,
            [Min(12, gpu: "GTX 1060 3GB · RX 580 4GB · DirectX 12", cpu: "Core i5-8400 · Ryzen 3 3300X"), Rec(16, gpu: "GTX 1070 8GB · RX Vega 56 8GB · DirectX 12", cpu: "Core i7-8700K · Ryzen 5 3600X")],
            "store.steampowered.com/app/1245620", null, "ER", "#c9a85c", ["elden ring", "الدن رینگ", "الدن"], "Soft_Purpose_EldenRing"),
        new("bg3", "Baldur's Gate 3", "Larian Studios", SoftCategory.Game, SoftGpu.Any,
            [Min(8, 4, gpu: "GTX 970 · RX 480 · Arc A380 · DirectX 11", cpu: "Core i5 4690 · FX 8350"), Rec(16, 8, gpu: "RTX 2060 Super · RX 5700 XT · Arc A580 · DirectX 11", cpu: "Core i7 8700K · Ryzen 5 3600")],
            "store.steampowered.com/app/1086940", null, "BG", "#8c1d1d", ["baldur's gate", "baldurs gate", "bg3", "بالدورز گیت", "بالدر"], "Soft_Purpose_Bg3", "Soft_Note_Ssd"),
        new("wukong", "Black Myth: Wukong", "Game Science", SoftCategory.Game, SoftGpu.Any,
            [Min(16, gpu: "GTX 1060 6GB · RX 580 8GB · DirectX 11", cpu: "Core i5-8400 · Ryzen 5 1600"), Rec(16, gpu: "RTX 2060 · RX 5700 XT · Arc A750 · DirectX 12", cpu: "Core i7-9700 · Ryzen 5 5500")],
            "store.steampowered.com/app/2358720", null, "BM", "#a8833a", ["wukong", "black myth", "ووکانگ", "بلک میث"], "Soft_Purpose_Wukong", "Soft_Note_Wukong"),
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
