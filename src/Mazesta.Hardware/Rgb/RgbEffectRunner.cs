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

    /// <summary>The shortest time between two frames of a device: memory (SMBus) and a graphics card (I2C) take tens of milliseconds for every write and share their bus with the
    /// sensors, so they get a few frames a second; a board or a strip on USB, more.</summary>
    private static int Interval(RgbKind kind) => kind is RgbKind.Memory or RgbKind.Graphics ? 200 : 66;

    private async Task Loop(CancellationToken ct)
    {
        var clock = Stopwatch.StartNew(); long checkedAt = -1000; bool away = false; var due = new Dictionary<int, long>(); var last = new Dictionary<int, RgbColor[]>();
        try
        {
            while (true)
            {
                if (clock.ElapsedMilliseconds - checkedAt >= 1000) { checkedAt = clock.ElapsedMilliseconds; bool y = Yield?.Invoke() == true; if (y != away) { away = y; if (y) lock (_gate) _begun.Clear(); } }
                if (away) { await Task.Delay(900, ct).ConfigureAwait(false); continue; }   // another process runs the lights: look again in a second
                if (!client.Connected) { await Task.Delay(FrameMs, ct).ConfigureAwait(false); continue; }
                List<RgbJob> jobs; lock (_gate) jobs = [.. _jobs.Values];
                if (jobs.Count == 0) return;
                long now = clock.ElapsedMilliseconds; bool sent = false;
                foreach (var j in jobs)
                {
                    if (due.TryGetValue(j.Device, out long at) && now < at) continue;
                    due[j.Device] = now + Interval(client.Device(j.Device)?.Kind ?? RgbKind.Other);
                    bool first; lock (_gate) first = _begun.Add(j.Device);
                    if (first) last.Remove(j.Device);
                    if (first && !await client.EnterDirectAsync(j.Device, ct).ConfigureAwait(false)) { Remove(j.Device); continue; }
                    var frame = RgbFrames.Frame(j.Effect, clock.Elapsed.TotalSeconds, j.Leds, j.Color, j.Palette, j.Speed, j.Brightness);
                    if (last.TryGetValue(j.Device, out var was) && was.AsSpan().SequenceEqual(frame)) continue;   // the same colours again: nothing to write
                    last[j.Device] = frame; sent = true;
                    await client.PushLedsAsync(j.Device, frame, ct).ConfigureAwait(false);
                }
                if (sent) await client.SyncAsync(ct).ConfigureAwait(false);   // the next frames wait until the server has written these
                long next = jobs.Min(j => due.GetValueOrDefault(j.Device)) - clock.ElapsedMilliseconds;
                await Task.Delay((int)Math.Clamp(next, 15, 250), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or IOException or InvalidOperationException or ObjectDisposedException) { log.LogInformation(e, "The lights' effects stopped: the connection is gone"); }
    }

    public void Dispose() => Stop();
}
