using Mazesta.Hardware.Audio; using Xunit; using Xunit.Abstractions;
namespace Mazesta.Hardware.Tests;

/// <summary>The machine's own sound: its output devices and the programs on the default one; Windows' sounds set and put back.</summary>
[Trait("Category", "Hardware")]
public class AppVolumesHardwareTests(ITestOutputHelper output)
{
    [Fact] public void Devices_and_programs_are_read_and_a_programs_volume_is_set_and_put_back()
    {
        var devices = AppVolumes.Devices();
        foreach (var d in devices) output.WriteLine($"{(d.Default ? "*" : " ")} {d.Name}: {d.Volume:P0}{(d.Muted ? " muted" : "")}");
        Assert.Single(devices, d => d.Default);
        var apps = AppVolumes.Apps();
        foreach (var a in apps) output.WriteLine($"  {(a.System ? "[Windows sounds]" : a.Name)} ({string.Join(",", a.Pids)}) {a.Volume:P0}{(a.Muted ? " muted" : "")}{(a.Playing ? " playing" : "")} {a.Path}");
        foreach (var line in AppVolumes.Describe()) output.WriteLine("    session " + line);
        var system = Assert.Single(apps, a => a.System);
        try
        {
            AppVolumes.SetApp("system", 0.37f);
            Assert.Equal(0.37f, AppVolumes.Apps().Single(a => a.System).Volume, 2);
        }
        finally { AppVolumes.SetApp("system", system.Volume); }
        Assert.Equal(system.Volume, AppVolumes.Apps().Single(a => a.System).Volume, 2);
    }

    [Fact] public void A_programs_volume_is_set_and_put_back_from_a_window_thread()
    {
        if (AppVolumes.Apps().FirstOrDefault(a => !a.System) is not { } app) return;   // nothing playing on this machine now
        var done = new ManualResetEventSlim(); Exception? failed = null; float seen = -1;
        var t = new Thread(() =>   // as the tray calls it: from a single-threaded (STA) window thread
        {
            try { AppVolumes.SetApp(app.Key, 0.42f); seen = AppVolumes.Apps().Single(a => a.Key == app.Key).Volume; }
            catch (Exception e) { failed = e; }
            finally { AppVolumes.SetApp(app.Key, app.Volume); done.Set(); }
        });
        t.SetApartmentState(ApartmentState.STA); t.Start(); done.Wait(10_000);
        output.WriteLine($"{app.Name}: set 0.42 on an STA thread, read {seen:0.00}; now {AppVolumes.Apps().Single(a => a.Key == app.Key).Volume:0.00} (was {app.Volume:0.00})");
        Assert.Null(failed); Assert.Equal(0.42f, seen, 2);
    }
}
