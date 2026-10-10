namespace Mazesta.App;

/// <summary>The shop's messages to this computer (see <see cref="MessageInbox"/>): the page lists them, marks them read and opens their link; a new one is announced as a notification.</summary>
public sealed partial class WebBridge
{
    private void RegisterMessages()
    {
        var inbox = Program.Inbox; if (inbox is null) return;
        object State() => new { unread = inbox.Unread, items = inbox.List().Select(m => new { id = m.Id, title = m.Title, body = m.Body, link = m.Link, at = m.At, read = m.Read }) };
        Method("messages.state", _ => State());
        Method("messages.read", p => { inbox.MarkRead(p.TryGetProperty("id", out var id) && id.ValueKind == System.Text.Json.JsonValueKind.Number ? id.GetInt64() : null); return State(); });
        Method("messages.open", p => { long id = p.GetProperty("id").GetInt64(); if (inbox.List().FirstOrDefault(m => m.Id == id)?.Link is { } link && ShopFeed.IsShopLink(link)) Open(link); return null; });
        void OnChanged() => _window.Dispatcher.BeginInvoke(() => Push("messages", State()));
        void OnArrived(ShopMessage m) => _notifier?.Tell(m.Title.Length > 0 ? m.Title : "Mazesta", m.Body.Length > 200 ? m.Body[..200] + "…" : m.Body);
        inbox.Changed += OnChanged; inbox.Arrived += OnArrived;
        _cleanup.Add(() => { inbox.Changed -= OnChanged; inbox.Arrived -= OnArrived; });
        _ = inbox.PollAsync();
    }
}
