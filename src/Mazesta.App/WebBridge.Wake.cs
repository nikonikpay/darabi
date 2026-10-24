using System.IO; using System.Text.Json;
using Mazesta.Diagnostics.Windows;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    /// <summary>
    /// What wakes the computer from sleep: devices, wake timers, Windows' maintenance, scheduled tasks and Wake-on-LAN, each a switch, and one button
    /// that turns them all off (its changes are kept in a file so one more button puts exactly those back). Every call answers with the state read
    /// back from Windows, never with what was asked for.
    /// </summary>
    private void RegisterWake()
    {
        var runner = _sp.GetRequiredService<ICommandRunner>();
        string undoFile = Path.Combine(_paths.ConfigDir, "wake-undo.json");

        WakeUndo? LoadUndo()
        {
            try { return File.Exists(undoFile) ? JsonSerializer.Deserialize<WakeUndo>(File.ReadAllText(undoFile)) : null; }
            catch (Exception e) when (e is IOException or JsonException) { return null; }
        }
        async Task<object> State(string? error = null, IReadOnlyList<string>? errors = null)
        {
            var s = await WakeControl.ReadAsync(runner, CancellationToken.None).ConfigureAwait(false);
            return new
            {
                devices = s.Devices.Select(d => new { name = d.Name, kind = d.Kind.ToString().ToLowerInvariant(), armed = d.Armed }),
                timers = new { ac = s.TimersAc, dc = s.TimersDc }, maintenance = s.Maintenance,
                tasks = s.Tasks.Select(t => new { path = t.Path, name = t.Name }), nics = s.Nics.Select(n => new { name = n.Name, description = n.Description, magic = n.Magic, pattern = n.Pattern }),
                active = s.Timers, last = s.LastWake, readError = s.Error, canUndo = LoadUndo() is not null, error, errors = errors ?? [],
            };
        }
        async Task<object?> Done(string what, string? error) { _log.LogInformation("Wake: {What}: {Result}", what, error ?? "ok"); return await State(error).ConfigureAwait(false); }

        MethodAsync("wake.state", async _ => await State().ConfigureAwait(false));
        MethodAsync("wake.device", async p => { string n = Str(p, "name"); bool on = Bool(p, "on"); return await Done($"device {n} {(on ? "on" : "off")}", await WakeControl.SetDeviceAsync(runner, n, on, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false); });
        MethodAsync("wake.timers", async p =>
        {
            int.TryParse(Str(p, "value"), out int v);
            return await Done($"wake timers {v}", await WakeControl.SetTimersAsync(runner, v, v, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
        });
        MethodAsync("wake.maintenance", async p =>
        {
            bool on = Bool(p, "on"); string? error = null;
            try { WakeControl.SetMaintenanceWake(on ? 1 : 0); } catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException) { error = e.Message; }
            return await Done($"maintenance wake {(on ? "on" : "off")}", error).ConfigureAwait(false);
        });
        MethodAsync("wake.task", async p => await Done($"task {Str(p, "name")} off", await WakeControl.SetTaskAsync(runner, new(Str(p, "path"), Str(p, "name")), false, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false));
        MethodAsync("wake.nic", async p =>
        {
            bool on = Bool(p, "on");
            return await Done($"Wake-on-LAN {Str(p, "name")} {(on ? "on" : "off")}", await WakeControl.SetNicAsync(runner, Str(p, "name"), on, on, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
        });
        MethodAsync("wake.all", async _ =>
        {
            var before = await WakeControl.ReadAsync(runner, CancellationToken.None).ConfigureAwait(false);
            var (undo, errors) = await WakeControl.DisableAllAsync(runner, before, CancellationToken.None).ConfigureAwait(false);
            // A second press must not overwrite what the first one kept: merge into the file's content, so "put back" restores the owner's own state.
            var kept = LoadUndo();
            if (kept is not null) undo = new([.. kept.Devices.Union(undo.Devices)], [.. kept.Tasks.Union(undo.Tasks)], kept.TimersAc ?? undo.TimersAc, kept.TimersDc ?? undo.TimersDc, kept.Maintenance ?? undo.Maintenance, [.. kept.Nics.Concat(undo.Nics.Where(n => kept.Nics.All(k => k.Name != n.Name)))]);
            try { Directory.CreateDirectory(_paths.ConfigDir); File.WriteAllText(undoFile, JsonSerializer.Serialize(undo)); }
            catch (IOException e) { _log.LogWarning(e, "Wake undo file not written"); errors.Add(e.Message); }
            _log.LogInformation("Wake: everything off ({Errors} failures)", errors.Count);
            return await State(null, errors).ConfigureAwait(false);
        });
        MethodAsync("wake.undo", async _ =>
        {
            var undo = LoadUndo();
            if (undo is null) return await State().ConfigureAwait(false);
            var errors = await WakeControl.RestoreAsync(runner, undo, CancellationToken.None).ConfigureAwait(false);
            if (errors.Count == 0) { try { File.Delete(undoFile); } catch (IOException) { } }
            _log.LogInformation("Wake: put back ({Errors} failures)", errors.Count);
            return await State(null, errors).ConfigureAwait(false);
        });
    }
}
