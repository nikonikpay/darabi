using System.Text.Json.Nodes;
namespace Mazesta.Core.Access;

/// <summary>
/// What the shop's site says this copy may use, kept so it also works offline. Today the answer is always "everything" (<see cref="Enforce"/> is false and nothing is locked): the
/// program is free. The site can later lock menus ("menu:Nav_Group_Tests") and tabs ("page:lights") for accounts without the Pro plan; a copy with this class asks the site and obeys.
/// <see cref="Open"/> pages are never locked, so a customer can always sign in, update, read the shop's messages and reach support.
/// </summary>
public sealed record Entitlements(bool Enforce, bool Login, string Plan, DateTimeOffset? Until, string? Account, IReadOnlyList<string> Locks, IReadOnlyList<string> FreeLocks, string Message)
{
    public static readonly Entitlements Free = new(false, false, "free", null, null, [], [], "");
    public static readonly IReadOnlyList<string> Open = ["dashboard", "messages", "settings", "appupdate", "help"];

    /// <summary>The locks that apply at <paramref name="now"/>: a Pro plan whose end date has passed (the site could not be asked since) falls back to the free locks.</summary>
    public IReadOnlyList<string> Effective(DateTimeOffset now) => Plan == "pro" && Until is { } u && u <= now ? FreeLocks : Locks;

    /// <summary>Whether a page (a tab of its menu) is locked: the page itself by "page:id", or its menu by "menu:key".</summary>
    public bool IsLocked(string pageId, string menuKey, DateTimeOffset now)
    {
        if (!Enforce || Open.Contains(pageId)) return false;
        var l = Effective(now); return l.Contains("page:" + pageId) || l.Contains("menu:" + menuKey);
    }

    /// <summary>The site's answer ({v, enforce, login, plan, until, account, locks, freeLocks, message}); anything not understood is "free" (nothing locked), never a lock.</summary>
    public static Entitlements Parse(string? json)
    {
        try
        {
            if (JsonNode.Parse(json ?? "") is not JsonObject o) return Free;
            static List<string> List(JsonNode? n) => [.. (n as JsonArray ?? []).Select(x => x?.GetValue<string>()).OfType<string>().Where(s => s.Length is > 0 and < 80)];
            var locks = List(o["locks"]);
            DateTimeOffset? until = DateTimeOffset.TryParse(o["until"]?.GetValue<string>(), out var u) ? u : null;
            string message = (o["message"]?.GetValue<string>() ?? "").Trim(); if (message.Length > 300) message = message[..300];
            return new(o["enforce"]?.GetValue<bool>() == true, o["login"]?.GetValue<bool>() == true, o["plan"]?.GetValue<string>() == "pro" ? "pro" : "free", until, o["account"]?.GetValue<string>(),
                locks, o["freeLocks"] is null ? locks : List(o["freeLocks"]), message);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or FormatException) { return Free; }
    }
}
