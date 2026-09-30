using System.IO; using System.Text.Json; using System.Text.Json.Serialization; using Mazesta.Core.Inventory; using Mazesta.Hardware.Details; using Mazesta.Persistence;
using Microsoft.Extensions.Logging;
namespace Mazesta.Desktop.Composition;

/// <summary>
/// The parts as last read, kept in Data/cache/hardware.json, so the hardware pages draw at once on the next start instead of waiting for WMI and the
/// sensor driver (the SPD needs its SMBus). The stored read is shown only while <see cref="HardwareFingerprint"/> and the app version are unchanged,
/// and only until this start's own read is done: that read still runs (drive counters, links, driver versions and IPs move) and replaces it, and
/// <see cref="Fresh"/> tells the pages to redraw. Reports and tests never use it; they await the live caches.
/// </summary>
public sealed class HardwareSnapshot(InventoryCache inventory, HardwareDetailsCache details, AppPaths paths, ILogger<HardwareSnapshot> log)
{
    internal sealed record Stored(string Key, DateTimeOffset ReadUtc, HardwareInventory Inventory, HardwareDetails Details);

    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private readonly object _lock = new();
    private Task<Stored?>? _stored; private Task<(HardwareInventory, HardwareDetails)>? _live;
    private string File => Path.Combine(paths.CacheDir, "hardware.json");

    /// <summary>Raised once, on a worker thread, when this start's own read is done.</summary>
    public event Action? Fresh;

    /// <summary>The inventory for a page, and whether it is the stored one.</summary>
    public async Task<(HardwareInventory Inventory, bool Cached)> InventoryAsync()
    {
        var (stored, _) = Start();
        if (inventory.IsLoaded || await stored.ConfigureAwait(false) is not { } s) return (await inventory.GetAsync().ConfigureAwait(false), false);
        return (s.Inventory, true);
    }

    /// <summary>The inventory and details for the specification pages, and whether they are the stored ones.</summary>
    public async Task<(HardwareInventory Inventory, HardwareDetails Details, bool Cached)> DetailsAsync()
    {
        var (stored, live) = Start();
        if (live.IsCompletedSuccessfully || await stored.ConfigureAwait(false) is not { } s) { var (i, d) = await live.ConfigureAwait(false); return (i, d, false); }
        return (s.Inventory, s.Details, true);
    }

    private (Task<Stored?>, Task<(HardwareInventory, HardwareDetails)>) Start()
    {
        lock (_lock)
        {
            if (_live is null) { var key = Task.Run(Key); _stored = key.ContinueWith(k => k.IsCompletedSuccessfully ? Load(k.Result) : null, TaskScheduler.Default); _live = ReadAsync(key); }
            return (_stored!, _live);
        }
    }

    // The app's full version (with its commit) is part of the key: a stored read from another build may lack fields this one shows.
    private static readonly string AppVersion = typeof(HardwareSnapshot).Assembly.GetCustomAttributes(false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "";
    private static string Key() => $"{AppVersion}|{HardwareFingerprint.Read()}";

    private Stored? Load(string key)
    {
        try
        {
            if (!System.IO.File.Exists(File)) return null;
            var s = JsonSerializer.Deserialize<Stored>(System.IO.File.ReadAllText(File), Json);
            if (s?.Key == key) return s;
            log.LogInformation("Stored hardware read is for other parts; not shown"); return null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            log.LogWarning(ex, "Stored hardware read unreadable; not shown"); return null;
        }
    }

    private async Task<(HardwareInventory, HardwareDetails)> ReadAsync(Task<string> key)
    {
        var inv = await inventory.GetAsync().ConfigureAwait(false); var d = await details.GetAsync().ConfigureAwait(false);
        try
        {
            // The key is taken again after the read: a part plugged in meanwhile must not be stored under the old key.
            string after = Key();
            if (after == await key.ConfigureAwait(false))
            {
                Directory.CreateDirectory(paths.CacheDir); string tmp = File + ".tmp";
                await System.IO.File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(new Stored(after, DateTimeOffset.UtcNow, inv, d), Json)).ConfigureAwait(false);
                System.IO.File.Move(tmp, File, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or JsonException) { log.LogWarning(ex, "Could not store the hardware read"); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { log.LogWarning(ex, "Hardware fingerprint failed; read not stored"); }
        Fresh?.Invoke();
        return (inv, d);
    }
}
