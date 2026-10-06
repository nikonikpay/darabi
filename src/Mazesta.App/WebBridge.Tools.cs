using Mazesta.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    private void RegisterTools()
    {
        // Windows Tools is a session singleton (a repair keeps running while the page is closed); its output streams to the page line by line.
        var tools = _sp.GetRequiredService<WindowsToolsViewModel>();
        object ToolsState() => new
        {
            busy = tools.IsBusy, status = tools.Status, percent = tools.Percent, canCancel = tools.CanCancel,
            output = tools.Output.TakeLast(400), pageFile = tools.PageFile.Select(r => new { label = r.Label, value = r.Value }),
        };
        Mirror("tools", tools, ToolsState, tools.Output);
        Method("tools.state", _ => ToolsState());
        MethodAsync("tools.exec", async p =>
        {
            switch (Str(p, "cmd"))
            {
                case "sfc": if (tools.SfcCommand.CanExecute(null)) await tools.SfcCommand.ExecuteAsync(null); break;
                case "dismScan": if (tools.DismScanCommand.CanExecute(null)) await tools.DismScanCommand.ExecuteAsync(null); break;
                case "dismRestore": if (tools.DismRestoreCommand.CanExecute(null)) await tools.DismRestoreCommand.ExecuteAsync(null); break;
                case "cancel": if (tools.CancelCommand.CanExecute(null)) tools.CancelCommand.Execute(null); break;
                case "cleanup": tools.DiskCleanupCommand.Execute(null); break;
                case "update": tools.WindowsUpdateCommand.Execute(null); break;
                case "pagefile": tools.PageFileSettingsCommand.Execute(null); break;
                default: throw new ArgumentException("unknown command");
            }
            return null;
        });

        // Gaming reads the power plans when made; made on first visit and kept.
        GamingViewModel? gaming = null;
        GamingViewModel Gaming()
        {
            if (gaming is not null) return gaming;
            gaming = _sp.GetRequiredService<Func<GamingViewModel>>()();
            Mirror("gaming", gaming, GamingState, gaming.Plans);
            return gaming;
        }
        object GamingState() => new
        {
            status = gaming!.Status, gameMode = gaming.GameMode, gpuScheduling = gaming.GpuScheduling,
            plans = gaming.Plans.Select((p, i) => new { index = i, name = p.Name, active = p.IsActive }), hasUltimate = gaming.HasUltimate,
        };
        MethodAsync("gaming.state", async _ => { var g = Gaming(); await g.Loaded.ConfigureAwait(true); return GamingState(); });
        Method("gaming.exec", p =>
        {
            var g = Gaming();
            switch (Str(p, "cmd"))
            {
                case "activate": if (int.TryParse(Str(p, "index"), out int i) && i >= 0 && i < g.Plans.Count) g.ActivateCommand.Execute(g.Plans[i]); break;
                case "gameMode": g.OpenGameModeCommand.Execute(null); break;
                case "graphics": g.OpenGraphicsCommand.Execute(null); break;
                case "ultimate": g.AddUltimateCommand.Execute(null); break;
                default: throw new ArgumentException("unknown command");
            }
            return null;
        });
    }
}
