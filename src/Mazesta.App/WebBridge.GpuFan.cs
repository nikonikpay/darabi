using System.Text.Json; using Mazesta.Core.Fans;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    /// <summary>The graphics card's fan curve, as the Tuning page asks for it; the control itself is <see cref="GpuFanController"/>, which lives as long as the app does.</summary>
    private void RegisterGpuFan()
    {
        Method("gpufan.state", _ => GpuFanController.Current.State());
        Method("gpufan.set", p =>
        {
            var raw = p.TryGetProperty("points", out var pts) && pts.ValueKind == JsonValueKind.Array ? pts.EnumerateArray().Where(a => a.GetArrayLength() == 2).Select(a => new FanPoint(a[0].GetDouble(), a[1].GetDouble())).ToList() : null;
            GpuFanController.Current.Set(Str(p, "mode"), Str(p, "source"), raw);
            return GpuFanController.Current.State();
        });
        Method("gpufan.reset", _ => { GpuFanController.Current.Reset(); return GpuFanController.Current.State(); });
        Method("gpufan.profile", p => GpuFanController.Current.Apply(Str(p, "name")) ? GpuFanController.Current.State() : GpuFanController.Current.State(Mazesta.Desktop.Localization.Loc.Get("Fans_Profile_NotNow")));
        Method("gpufan.profile.save", p => { GpuFanController.Current.Save(Str(p, "name")); return GpuFanController.Current.State(); });
        Method("gpufan.profile.delete", p => { GpuFanController.Current.Delete(Str(p, "name")); return GpuFanController.Current.State(); });
    }
}
