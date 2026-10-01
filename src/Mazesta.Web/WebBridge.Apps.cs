using Mazesta.Core.Software; using Mazesta.Desktop.Localization;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    /// <summary>
    /// The programs page: each program in <see cref="SoftwareCatalog"/> judged against this computer by its publisher's tiers, with what the tier
    /// it reaches suits and what the next one lacks. The same verdicts the assistant gives (<c>check_software</c>), from the same reading.
    /// </summary>
    private void RegisterApps(Func<Task<SoftMachine>> machine)
    {
        static double? Gb(long? b) => b is { } x ? Math.Round(x / 1073741824.0, 1) : null;
        MethodAsync("apps.state", async _ =>
        {
            var pc = await machine().ConfigureAwait(true);
            return new
            {
                machine = new { cpu = pc.CpuName, cores = pc.Cores, threads = pc.Threads, ram = Gb(pc.RamBytes), gpu = pc.GpuName, vram = Gb(pc.VramBytes), rt = pc.Dxr ?? SoftwareCatalog.RayTracing(pc.GpuName) },
                categories = Enum.GetValues<SoftCategory>().Select(c => new { id = c.ToString(), name = Loc.Get("Soft_Category_" + c) }),
                apps = SoftwareCatalog.Apps.Select(a =>
                {
                    var v = SoftwareCatalog.Judge(a, pc); var tier = v.Reached;
                    return new
                    {
                        id = a.Id, name = a.Name, vendor = a.Vendor, category = a.Category.ToString(), icon = a.Icon, mono = a.Mono, color = a.Color,
                        purpose = Loc.Get(a.PurposeKey), note = a.NoteKey is null ? null : Loc.Get(a.NoteKey), source = a.Source,
                        gpuNeed = a.Gpu.ToString(), level = v.Level?.ToString(), label = SoftwareCatalog.LevelName(a, v), next = v.Next?.ToString(), nextName = v.NextTier is { } nt ? TierName(nt) : null, missing = v.Missing.Select(ShortText),
                        @unchecked = (v.Unchecked ?? []).Select(x => Loc.Get("Soft_Unchecked_" + x)),
                        suits = tier is null ? null : Loc.Get(a.Tiers.Count == 1 ? "Soft_Scale_Single" : tier.ScaleKey ?? $"Soft_Scale_{a.Category}_{tier.Kind}"),
                        tiers = a.Tiers.Select(t => new
                        {
                            kind = t.Kind.ToString(), name = TierName(t), ram = t.RamGb, vram = t.VramGb, cores = t.Cores, rt = t.RayTracing || a.Gpu == SoftGpu.RayTracing, gpu = t.Gpu, cpu = t.Cpu,
                            suits = Loc.Get(t.ScaleKey ?? $"Soft_Scale_{a.Category}_{t.Kind}"), met = SoftwareCatalog.Lacks(a, t, pc).Count == 0,
                        }),
                    };
                }),
            };
        });
    }
}
