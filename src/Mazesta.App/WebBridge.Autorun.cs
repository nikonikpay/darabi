using System.IO; using System.Text.Json;
using Mazesta.Core.Gaming; using Mazesta.Diagnostics.Windows;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    private sealed record AutorunHeld(string Name, bool Running);

    /// <summary>
    /// What starts with Windows (see <see cref="Autorun"/>): the programs' own services, which the user ticks and switches off (what was switched off is kept in a file so one
    /// button puts exactly those back), and the startup programs, each a switch. Every answer is read back from Windows.
    /// </summary>
    private void RegisterAutorun()
    {
        var services = _sp.GetRequiredService<IServiceControl>();
        string undoFile = Path.Combine(_paths.ConfigDir, "autorun-undo.json");

        List<AutorunHeld> Held()
        {
            try { return File.Exists(undoFile) ? JsonSerializer.Deserialize<List<AutorunHeld>>(File.ReadAllText(undoFile)) ?? [] : []; }
            catch (Exception e) when (e is IOException or JsonException) { return []; }
        }
        void KeepHeld(List<AutorunHeld> held)
        {
            try { Directory.CreateDirectory(_paths.ConfigDir); if (held.Count == 0) File.Delete(undoFile); else File.WriteAllText(undoFile, JsonSerializer.Serialize(held)); }
            catch (IOException e) { _log.LogWarning(e, "Autorun undo file not written"); }
        }
        object State(IReadOnlyList<string>? errors = null) => new
        {
            services = Autorun.ReadServices(services).Select(s => new { name = s.Name, display = s.Display, note = s.Note, path = s.Path, running = s.Running }),
            startup = Autorun.ReadStartup().Select(s => new { id = s.Id, name = s.Name, command = s.Command, source = s.Source, enabled = s.Enabled }),
            held = Held().Select(h => h.Name), errors = errors ?? [],
        };

        MethodAsync("autorun.state", _ => Task.Run<object?>(() => State()));
        MethodAsync("autorun.services", p => Task.Run<object?>(() =>
        {
            var names = p.TryGetProperty("names", out var arr) && arr.ValueKind == JsonValueKind.Array ? arr.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).Distinct().ToList() : [];
            var held = Held(); var errors = new List<string>(); var listed = Autorun.ReadServices(services);
            foreach (string name in names)
            {
                bool running = listed.FirstOrDefault(s => s.Name == name)?.Running == true;
                string? error = Autorun.DisableService(services, name);
                if (error is null) { if (held.All(h => h.Name != name)) held.Add(new(name, running)); } else errors.Add($"{name}: {error}");
            }
            KeepHeld(held); _log.LogInformation("Autorun: {Count} service(s) switched off, {Errors} failure(s)", names.Count - errors.Count, errors.Count);
            return State(errors);
        }));
        MethodAsync("autorun.undo", _ => Task.Run<object?>(() =>
        {
            var held = Held(); var errors = new List<string>(); var left = new List<AutorunHeld>();
            foreach (var h in held) { string? error = Autorun.RestoreService(services, h.Name, h.Running); if (error is not null) { errors.Add($"{h.Name}: {error}"); left.Add(h); } }
            KeepHeld(left); _log.LogInformation("Autorun: {Count} service(s) put back, {Errors} failure(s)", held.Count - left.Count, errors.Count);
            return State(errors);
        }));
        MethodAsync("autorun.startup", p => Task.Run<object?>(() =>
        {
            string id = Str(p, "id"); bool on = Bool(p, "on"); string? error = Autorun.SetStartup(id, on);
            _log.LogInformation("Autorun: startup {Id} {State}: {Result}", id, on ? "on" : "off", error ?? "ok");
            return State(error is null ? null : [error]);
        }));
    }
}
