using System.Diagnostics; using System.IO; using System.IO.Compression;
using Mazesta.Core.Inventory; using Mazesta.Desktop.Composition; using Mazesta.Desktop.ViewModels; using Mazesta.Monitoring; using Mazesta.Persistence;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    private void RegisterDiagnostics()
    {
        var engine = _sp.GetRequiredService<PollingEngine>(); var inventory = _sp.GetRequiredService<InventoryCache>();

        // What the first polls found missing or unreadable on this machine, for the Settings page.
        Method("diag.state", _ =>
        {
            var (findings, notes) = App.Recorder?.Current() ?? (HardwareDiagnosticsReport.Problems(engine.Hardware, new Dictionary<Core.Hardware.SensorId, SensorTally>()), []);
            return new { findings, notes, logs = _paths.LogsDir, polled = App.Recorder is not null };
        });

        // One zip to bring back: the app's logs, the hardware report, the inventory as text, the settings and the tray's checks. Nothing is sent
        // anywhere; the folder opens with the file selected.
        MethodAsync("diag.export", async _ =>
        {
            App.Recorder?.Write();
            App.LogProvider?.Flush();
            HardwareInventory? inv = null;
            try { inv = await inventory.GetAsync().ConfigureAwait(true); } catch (Exception e) when (e is not OutOfMemoryException) { _log.LogWarning(e, "Inventory for the export failed"); }
            string dir = Path.Combine(_paths.DataRoot, "diagnostics"); Directory.CreateDirectory(dir);
            string zip = Path.Combine(dir, $"mazesta-diag-{Environment.MachineName}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            await Task.Run(() =>
            {
                using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
                foreach (var f in Directory.EnumerateFiles(_paths.LogsDir)) Add(archive, f, "logs/" + Path.GetFileName(f));
                Add(archive, _paths.ConfigFile, "config/appconfig.json");
                Add(archive, TrayCheckLog.FileIn(_paths), "tray/checks.json");
                if (inv is not null)
                {
                    var text = string.Join(Environment.NewLine + Environment.NewLine, SystemInfoViewModel.Describe(inv).Select(s => s.Title + Environment.NewLine + string.Join(Environment.NewLine, s.Rows.Select(r => $"  {r.Label}: {r.Value}"))));
                    if (inv.Errors.Count > 0) text += Environment.NewLine + Environment.NewLine + "Errors" + Environment.NewLine + string.Join(Environment.NewLine, inv.Errors);
                    using var w = new StreamWriter(archive.CreateEntry("inventory.txt").Open()); w.Write(text);
                }
            }).ConfigureAwait(true);
            _log.LogInformation("Diagnostics exported to {Zip}", zip);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zip}\"") { UseShellExecute = true })?.Dispose();
            return zip;
        });

        // The page's own errors go to the same log as the host's.
        Method("app.logError", p => { var m = Str(p, "message"); _log.LogWarning("Page error: {Message}", m.Length > 4000 ? m[..4000] : m); return null; });
    }

    /// <summary>Copies a file that may be open for writing (today's log is): read shared, so the app keeps logging while it is zipped.</summary>
    private static void Add(ZipArchive archive, string file, string name)
    {
        if (!File.Exists(file)) return;
        try
        {
            using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var dst = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
            src.CopyTo(dst);
        }
        catch (IOException) { }
    }
}
