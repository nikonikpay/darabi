using System.Diagnostics; using Microsoft.Extensions.Logging;
namespace Mazesta.Hardware.Rgb;

/// <summary>One device driven by the app: the effect, its colours and how many LEDs it has.</summary>
public sealed record RgbJob(int Device, string Effect, RgbColor Color, IReadOnlyList<RgbColor>? Palette, int Speed, int Brightness, int Leds);

/// <summary>
/// Runs the effects a device has not itself: every few hundredths of a second each such device gets the next frame of its effect, as a colour for each LED. Runs only while
/// there is a job, and only in one process at a time (<see cref="Yield"/> lets the tray step aside while the app is open). A broken connection ends the loop: whoever
/// connects again applies the scene again, which starts it again.
/// </summary>
public sealed class RgbEffectRunner(OpenRgbClient client, ILogger log) : IDisposable
{
    private const int FrameMs = 50;
    private readonly Dictionary<int, RgbJob> _jobs = []; private readonly HashSet<int> _begun = []; private readonly object _gate = new();   // _begun: the devices already put in their per-LED mode
    private CancellationTokenSource? _stop; private Task? _loop;
    /// <summary>Asked once a second: true while another process is running the lights, so this one sends nothing.</summary>
    public Func<bool>? Yield { get; set; }
    public bool Running => _loop is { IsCompleted: false };
    public int Count { get { lock (_gate) return _jobs.Count; } }

    public void Set(RgbJob job) { lock (_gate) { _jobs[job.Device] = job; _begun.Remove(job.Device); } }   // a new look puts the device in its mode again
    public void Remove(int device) { lock (_gate) { _jobs.Remove(device); _begun.Remove(device); } }
    public void Clear() { lock (_gate) { _jobs.Clear(); _begun.Clear(); } }

    /// <summary>Starts the loop when there is a job and none runs.</summary>
    public void Start()
    {
        if (Count == 0 || Running) return;
        _stop = new CancellationTokenSource(); var token = _stop.Token;
        _loop = Task.Run(() => Loop(token), CancellationToken.None);
    }

    public void Stop() { try { _stop?.Cancel(); } catch (ObjectDisposedException) { } try { _loop?.Wait(1000); } catch (AggregateException) { } _stop?.Dispose(); _stop = null; _loop = null; }

    private async Task Loop(CancellationToken ct)
    {
        var clock = Stopwatch.StartNew(); int tick = 0; bool away = false;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(FrameMs));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (tick++ % (1000 / FrameMs) == 0) { bool y = Yield?.Invoke() == true; if (y != away) { away = y; if (y) lock (_gate) _begun.Clear(); } }
                if (away) { await Task.Delay(900, ct).ConfigureAwait(false); continue; }   // another process runs the lights: look again in a second
                if (!client.Connected) continue;
                List<RgbJob> jobs; lock (_gate) jobs = [.. _jobs.Values];
                if (jobs.Count == 0) return;
                foreach (var j in jobs)
                {
                    bool first; lock (_gate) first = _begun.Add(j.Device);
                    if (first && !await client.EnterDirectAsync(j.Device, ct).ConfigureAwait(false)) { Remove(j.Device); continue; }
                    await client.PushLedsAsync(j.Device, RgbFrames.Frame(j.Effect, clock.Elapsed.TotalSeconds, j.Leds, j.Color, j.Palette, j.Speed, j.Brightness), ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or IOException or InvalidOperationException or ObjectDisposedException) { log.LogInformation(e, "The lights' effects stopped: the connection is gone"); }
    }

    public void Dispose() => Stop();
}
