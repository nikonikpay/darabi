using System.Text.Json; using Mazesta.Core.Fans;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    /// <summary>
    /// Fan control of the board's outputs, as the page asks for it; the control itself is <see cref="FanController"/>, which lives as long as the app does (also with the window
    /// closed, while the tray runs), so a profile keeps holding the fans. The page can set an output (auto, fixed or a curve), put a whole profile on, save the present settings as
    /// a profile, say what an output is, and have one output run at full for a few seconds to find out what it drives.
    /// </summary>
    private void RegisterFans()
    {
        Method("fans.state", _ => FanController.Current.State());
        Method("fans.set", p =>
        {
            var raw = p.TryGetProperty("points", out var pts) && pts.ValueKind == JsonValueKind.Array ? pts.EnumerateArray().Where(a => a.GetArrayLength() == 2).Select(a => new FanPoint(a[0].GetDouble(), a[1].GetDouble())).ToList() : null;
            double? percent = p.TryGetProperty("percent", out var pv) && pv.TryGetDouble(out var d) ? d : null;
            FanController.Current.Set(Str(p, "id"), Str(p, "mode"), percent, Str(p, "source"), raw);
            return FanController.Current.State();
        });
        Method("fans.reset", _ => { FanController.Current.Reset(); return FanController.Current.State(); });
        Method("fans.profile", p => FanController.Current.Apply(Str(p, "name")) ? FanController.Current.State() : FanController.Current.State(Mazesta.Desktop.Localization.Loc.Get("Fans_Profile_NotNow")));
        Method("fans.profile.save", p => { FanController.Current.Save(Str(p, "name")); return FanController.Current.State(); });
        Method("fans.profile.delete", p => { FanController.Current.Delete(Str(p, "name")); return FanController.Current.State(); });
        Method("fans.label", p => { FanController.Current.Label(Str(p, "id"), p.TryGetProperty("name", out _) ? Str(p, "name") : null, p.TryGetProperty("kind", out _) ? Str(p, "kind") : null); return FanController.Current.State(); });
        MethodAsync("fans.identify", async p => await FanController.Current.IdentifyAsync(Str(p, "id")).ConfigureAwait(true));
    }
}
