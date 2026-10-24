using System.Diagnostics; using System.Runtime.ExceptionServices;
namespace Mazesta.Diagnostics.Gpu.Benchmarks;

/// <summary>Runs a call that may never return (a video driver that stopped answering) on a thread of its own and waits for it with a limit, so that neither the
/// run nor the Cancel button is held by it. A thread left behind is a background one, and <c>abandoned</c> tells the caller not to release what it may still use.</summary>
internal static class StallGuard
{
    /// <summary>True with the answer when <paramref name="work"/> returned in time (its exception is thrown here, as it was); false when it did not within
    /// <paramref name="limit"/> seconds. Cancelling throws <see cref="OperationCanceledException"/> at once, without waiting for the call.</summary>
    public static bool Run<T>(Func<T> work, double limit, CancellationToken ct, ref bool abandoned, out T? value)
    {
        T? result = default; Exception? error = null; var done = new ManualResetEventSlim();   // not disposed: a thread left behind may still set it
        new Thread(() => { try { result = work(); } catch (Exception e) { error = e; } finally { done.Set(); } }) { IsBackground = true, Name = "Mazesta guarded call" }.Start();
        var clock = Stopwatch.StartNew();
        while (!done.Wait(100))
        {
            if (ct.IsCancellationRequested) { if (!done.Wait(3000)) abandoned = true; throw new OperationCanceledException(ct); }   // a call that looks at the token ends by itself; only one that does not is left
            if (clock.Elapsed.TotalSeconds > limit) { abandoned = true; value = default; return false; }
        }
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
        value = result; return true;
    }
}
