using System.Runtime.CompilerServices;
namespace Mazesta.Desktop.Composition;

/// <summary>
/// The UI thread, for the engines' events (raised on worker threads) and the view models that expect to run there: the few calls of WPF's
/// Dispatcher the app used (BeginInvoke, InvokeAsync, CheckAccess), over the Windows Forms message loop's synchronization context. The app
/// sets <see cref="Current"/> on its UI thread before anything is built. A fire-and-forget action that throws is reported to
/// <see cref="Unhandled"/> (the app logs it), as WPF's dispatcher reported it, so one bad handler does not end the app.
/// </summary>
public sealed class UiDispatcher(SynchronizationContext context, int threadId)
{
    public static UiDispatcher? Current { get; set; }
    /// <summary>The window a short-lived window (the PDF printer's) is centred on; null before the main window exists.</summary>
    public static System.Windows.Forms.IWin32Window? Owner { get; set; }
    public static event Action<Exception>? Unhandled;

    /// <summary>For the view models' constructors: queue <paramref name="action"/> on the UI thread (or run it at once in a host without one: tests).</summary>
    public static object Post(Action action) => Current is { } d ? d.BeginInvoke(action) : Run(action);
    private static object Run(Action action) { action(); return Task.CompletedTask; }

    public bool CheckAccess() => Environment.CurrentManagedThreadId == threadId;

    public Task BeginInvoke(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(_ => { try { action(); } catch (Exception e) { Unhandled?.Invoke(e); } finally { done.TrySetResult(); } }, null);
        return done.Task;
    }

    public Task BeginInvoke(Func<Task> action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(async _ => { try { await action(); } catch (Exception e) { Unhandled?.Invoke(e); } finally { done.TrySetResult(); } }, null);
        return done.Task;
    }

    public Operation InvokeAsync(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(_ => { try { action(); done.TrySetResult(); } catch (Exception e) { done.TrySetException(e); } }, null);
        return new(done.Task);
    }

    public Operation<T> InvokeAsync<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(_ => { try { done.TrySetResult(work()); } catch (Exception e) { done.TrySetException(e); } }, null);
        return new(done.Task);
    }

    /// <summary>What <see cref="InvokeAsync(Action)"/> returns: awaited directly, or its <see cref="Task"/> (as WPF's DispatcherOperation).</summary>
    public readonly record struct Operation(Task Task) { public TaskAwaiter GetAwaiter() => Task.GetAwaiter(); }
    public readonly record struct Operation<T>(Task<T> Task) { public TaskAwaiter<T> GetAwaiter() => Task.GetAwaiter(); }
}
