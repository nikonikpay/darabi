using System.Text.Json; using Mazesta.Core.Access; using Mazesta.Persistence.Updates;
namespace Mazesta.App;

/// <summary>What the shop's site lets this copy use (see <see cref="AccessService"/>): the page asks which menus and tabs are locked and, only when the site offers it, signs in with a mobile number.
/// Today nothing is locked and no sign-in is offered, so the page shows nothing of this.</summary>
public sealed partial class WebBridge
{
    private void RegisterAccess()
    {
        var access = Program.Access;
        object State()
        {
            var e = access?.Current ?? Entitlements.Free;
            return new { enforce = e.Enforce, login = e.Login, plan = e.Plan, until = e.Until, account = e.Account, signedIn = access?.SignedIn == true, locks = e.Effective(DateTimeOffset.UtcNow), message = e.Message };
        }
        Method("access.state", _ => State());
        if (access is null) return;
        MethodAsync("access.code", async p => { await SiteGuard(() => access.CodeAsync(Str(p, "mobile"), CancellationToken.None)); return true; });
        MethodAsync("access.verify", async p => { await SiteGuard(() => access.VerifyAsync(Str(p, "mobile"), Str(p, "code"), CancellationToken.None)); return State(); });
        MethodAsync("access.logout", async _ => { await access.LogoutAsync(); return State(); });
        void OnChanged() => _window.Dispatcher.BeginInvoke(() => Push("access", State()));
        access.Changed += OnChanged; _cleanup.Add(() => access.Changed -= OnChanged);
        _ = access.RefreshAsync();
    }

    /// <summary>A site refusal reaches the page in the site's own words.</summary>
    private static async Task SiteGuard(Func<Task> work)
    {
        try { await work().ConfigureAwait(false); }
        catch (SiteException e) { throw new InvalidOperationException(e.Message, e); }
    }
}
