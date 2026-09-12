using System.Runtime.CompilerServices; using Mazesta.Desktop.Composition; using Microsoft.Extensions.DependencyInjection; using Xunit;
namespace Mazesta.Desktop.Tests;

/// <summary>
/// The page view models are IDisposable. A container tracks every IDisposable transient it creates
/// until the container is disposed, so registering them with AddTransient leaked one live view model
/// per navigation for the life of the process. They are registered as Func&lt;T&gt; factories instead;
/// these tests pin both halves of that contract.
/// </summary>
public class ViewModelFactoryTests
{
    private sealed class Probe : IDisposable { public bool Disposed; public void Dispose() => Disposed = true; }

    // Built in its own non-inlined frame so the last page created is not still rooted by a live
    // stack slot when the collection runs - only the WeakReferences come back.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<WeakReference> NavigateTwiceAndDrop(Func<Probe> nextPage)
    {
        var refs = new List<WeakReference>();
        for (int i = 0; i < 2; i++) { var page = nextPage(); page.Dispose(); refs.Add(new WeakReference(page)); }
        return refs;
    }

    [Fact] public void Factory_built_view_models_are_not_retained_by_the_container()
    {
        var s = new ServiceCollection();
        Bootstrapper.AddViewModelFactory(s, _ => new Probe());
        using var sp = s.BuildServiceProvider();
        var factory = sp.GetRequiredService<Func<Probe>>();

        // Two navigations. Each page is dropped (ShellViewModel disposes the outgoing page) before
        // the GC runs, so nothing may keep them alive.
        var refs = NavigateTwiceAndDrop(factory);

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.All(refs, r => Assert.False(r.IsAlive));
    }

    [Fact] public void Transient_registration_would_retain_them_which_is_why_the_factory_exists()
    {
        var s = new ServiceCollection();
        s.AddTransient<Probe>();
        using var sp = s.BuildServiceProvider();
        var refs = NavigateTwiceAndDrop(sp.GetRequiredService<Probe>);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.All(refs, r => Assert.True(r.IsAlive));   // still rooted by the container's disposable list
    }

    [Fact] public void Disposing_the_container_does_not_dispose_factory_built_view_models()
    {
        var s = new ServiceCollection();
        Bootstrapper.AddViewModelFactory(s, _ => new Probe());
        var sp = s.BuildServiceProvider();
        var page = sp.GetRequiredService<Func<Probe>>()();
        sp.Dispose();
        Assert.False(page.Disposed);
    }
}
