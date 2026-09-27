using Mazesta.Core.Tuning; using Mazesta.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    private void RegisterTuning()
    {
        // The same session-long view model as the WPF edition: its range checks, recovery journal, curve scan and automatic search all apply.
        // Its confirmations are the WPF edition's system dialogs, so a dangerous step is confirmed outside the page.
        var t = _sp.GetRequiredService<TuningViewModel>();
        object Tile(LiveTile x) => new { label = x.Label, value = x.Value };
        object State() => new
        {
            hasDevice = t.HasDevice, unavailable = t.Unavailable, devices = t.Devices.Select(d => d.Name), device = t.Device is { } dev ? t.Devices.ToList().IndexOf(dev) : -1,
            name = t.Device?.Name, ranges = t.Ranges, otherGpus = t.OtherGpus, status = t.Status,
            live = new[] { t.LiveCore, t.LiveMemory, t.LiveVoltage, t.LiveTemperature, t.LivePower, t.LiveFan }.Select(Tile),
            liveClock = double.IsNaN(t.LiveClockMHz) ? (double?)null : t.LiveClockMHz, liveVolt = double.IsNaN(t.LiveVoltageV) ? (double?)null : t.LiveVoltageV,
            form = new
            {
                core = t.CoreOffset, memory = t.MemoryOffset, lockClock = t.LockClock, maxClock = t.MaxClock, setPower = t.SetPower, power = t.PowerLimit,
                manualFan = t.ManualFan, fan = t.FanPercent, profileName = t.ProfileName, coreValue = t.CoreOffsetValue, capValue = t.CapValue,
            },
            limits = new
            {
                hasCore = t.HasCore, hasMemory = t.HasMemory, hasPower = t.HasPower, hasFan = t.HasFan, coreMin = t.CoreMin, coreMax = t.CoreMax, memoryMin = t.MemoryMin,
                memoryMax = t.MemoryMax, powerMin = t.PowerMin, powerMax = t.PowerMax, fanMin = t.FanMin, fanMax = t.FanMax, clockMax = t.ClockMax, clockMin = GpuTuningLimits.MinLockMHz,
            },
            curve = t.Curve?.Select(p => new { clock = p.ClockMHz, volt = p.VoltageV }), curveInfo = t.CurveInfo, curveEstimate = t.CurveEstimate, curveStatus = t.CurveStatus,
            busy = t.IsTuning, percent = t.AutoPercent, stepTitle = t.AutoStepTitle, stepSettings = t.AutoStepSettings, stepLoad = t.AutoStepLoad, result = t.AutoResult,
            log = t.AutoLog.Select(l => new { step = l.Step, kind = l.Kind, settings = l.Settings, result = l.Result, clean = l.Clean, problem = l.Problem }),
            profiles = t.Profiles.Select((p, i) => new { index = i, name = p.Name, kind = p.KindValue.ToString(), kindText = p.Kind, created = p.Created, summary = p.Summary, evidence = p.Evidence }),
            memory = t.Memory.Select(m => new { label = m.Label, value = m.Value }),
        };
        Mirror("tuning", t, State, t.AutoLog, t.Profiles);
        foreach (var tile in new[] { t.LiveCore, t.LiveMemory, t.LiveVoltage, t.LiveTemperature, t.LivePower, t.LiveFan }) tile.PropertyChanged += (_, _) => PushSoon("tuning", State);

        Method("tuning.state", _ => State());
        Method("tuning.visible", p => { t.SetVisible(Bool(p, "value")); return null; });
        Method("tuning.set", p =>
        {
            string v = Str(p, "value"); int Int() => int.TryParse(v, out int n) ? n : 0;
            switch (Str(p, "field"))
            {
                case "device": if (Int() is int i && i >= 0 && i < t.Devices.Count) t.Device = t.Devices[i]; break;
                case "core": t.CoreOffset = v; break;
                case "memory": t.MemoryOffset = v; break;
                case "lockClock": t.LockClock = Bool(p, "value"); break;
                case "maxClock": t.MaxClock = v; break;
                case "setPower": t.SetPower = Bool(p, "value"); break;
                case "power": t.PowerLimit = v; break;
                case "manualFan": t.ManualFan = Bool(p, "value"); break;
                case "fan": t.FanPercent = v; break;
                case "profileName": t.ProfileName = v; break;
                // The curve editor sets offset and cap together, as one gesture.
                case "curve": t.CoreOffset = Str(p, "core"); if (int.TryParse(Str(p, "cap"), out int cap)) t.CapValue = cap; break;
                default: throw new ArgumentException("unknown field");
            }
            return null;
        });
        MethodAsync("tuning.exec", async p =>
        {
            GpuProfileRow Profile() => int.TryParse(Str(p, "index"), out int i) && i >= 0 && i < t.Profiles.Count ? t.Profiles[i] : throw new ArgumentException("unknown profile");
            switch (Str(p, "cmd"))
            {
                case "apply": if (t.ApplyCommand.CanExecute(null)) t.ApplyCommand.Execute(null); break;
                case "reset": if (t.ResetCommand.CanExecute(null)) t.ResetCommand.Execute(null); break;
                case "saveProfile": t.SaveProfileCommand.Execute(null); break;
                case "applyProfile": var ap = Profile(); if (t.ApplyProfileCommand.CanExecute(ap)) t.ApplyProfileCommand.Execute(ap); break;
                case "loadProfile": t.LoadProfileCommand.Execute(Profile()); break;
                case "deleteProfile": t.DeleteProfileCommand.Execute(Profile()); break;
                case "scanCurve": if (t.ScanCurveCommand.CanExecute(null)) await t.ScanCurveCommand.ExecuteAsync(null); break;
                case "autoUndervolt": if (t.AutoUndervoltCommand.CanExecute(null)) await t.AutoUndervoltCommand.ExecuteAsync(null); break;
                case "autoOverclock": if (t.AutoOverclockCommand.CanExecute(null)) await t.AutoOverclockCommand.ExecuteAsync(null); break;
                case "cancel": if (t.CancelAutoCommand.CanExecute(null)) t.CancelAutoCommand.Execute(null); break;
                case "firmware": t.RestartToFirmwareCommand.Execute(null); break;
                default: throw new ArgumentException("unknown command");
            }
            return null;
        });
    }
}
