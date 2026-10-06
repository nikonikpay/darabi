using Mazesta.Core.Inventory; using Mazesta.Hardware.Wmi;
using Microsoft.Extensions.DependencyInjection;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    /// <summary>
    /// The laptop battery for the hands-on checks: what its controller reports (health is full capacity against design capacity), and a drain
    /// test, which is two readings of the remaining capacity and the time between them. The page asks while its card is open; nothing is read
    /// otherwise. A desktop has no battery and gets an empty list.
    /// </summary>
    private void RegisterBattery()
    {
        var wmi = _sp.GetRequiredService<IWmiQuery>();
        (BatteryInfo Info, DateTimeOffset At)? started = null;
        static object Row(BatteryInfo b) => new
        {
            name = b.Name, maker = b.Maker, healthPercent = b.HealthPercent, designMwh = b.DesignMwh, fullMwh = b.FullMwh, lostMwh = b.LostMwh, cycles = b.CycleCount,
            chargePercent = b.ChargePercent, charging = b.Charging, discharging = b.Discharging, onMains = b.OnMains, rateMw = b.RateMw, voltageMv = b.VoltageMv,
        };
        MethodAsync("battery.read", async _ => (await Task.Run(() => BatteryReader.Read(wmi)).ConfigureAwait(true)).Select(Row).ToList());
        // The drain test measures the first battery (laptops with two are rare, and their second is read the same way from the list above).
        MethodAsync("battery.test", async p =>
        {
            string cmd = Str(p, "cmd");
            if (cmd == "stop") { started = null; return null; }
            var now = (await Task.Run(() => BatteryReader.Read(wmi)).ConfigureAwait(true)).FirstOrDefault();
            if (now is null) { started = null; return new { state = "none" }; }
            if (now.OnMains == true || now.Discharging == false) { bool was = started is not null; started = null; return new { state = was ? "pluggedDuring" : "plugged", battery = Row(now) }; }
            if (cmd == "start" || started is null) { started = (now, DateTimeOffset.UtcNow); return new { state = "running", minutes = 0.0, battery = Row(now) }; }
            var elapsed = DateTimeOffset.UtcNow - started.Value.At;
            var drain = BatteryDrain.Between(started.Value.Info, now, elapsed);
            return new
            {
                state = "running", minutes = Math.Round(elapsed.TotalMinutes, 1), battery = Row(now),
                drain = drain is null ? null : new { usedMwh = drain.UsedMwh, meanWatts = drain.MeanWatts, fullChargeMinutes = drain.FullChargeMinutes },
            };
        });
    }
}
