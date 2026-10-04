using Mazesta.Desktop.Localization;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    private void RegisterAppUpdate()
    {
        var updater = AppUpdater.Get(_paths, _log);
        object State()
        {
            var m = updater.Manifest; var app = m?.App;
            return new
            {
                current = updater.Current, state = updater.State.ToString(), progress = updater.Progress, error = updater.Error,
                checkedAt = updater.CheckedAt?.ToString("yyyy/MM/dd HH:mm", Loc.Culture),
                latest = app is null ? null : new
                {
                    version = app.Version, size = app.Size, date = app.Date.ToLocalTime().ToString("yyyy/MM/dd", Loc.Culture),
                    notes = (Loc.IsRtl ? app.NotesFa ?? app.NotesEn : app.NotesEn ?? app.NotesFa) ?? "",
                },
                data = new
                {
                    lists = updater.DataLists, downloaded = updater.DataDownloaded, published = m?.Published.ToLocalTime().ToString("yyyy/MM/dd HH:mm", Loc.Culture),
                    syncedAt = updater.DataSyncedAt?.ToString("yyyy/MM/dd HH:mm", Loc.Culture),
                },
                site = AppUpdater.Folder.ToString(),
                // The release on the site is the users' edition: installing it over the company's own copy would turn it into one.
                canInstall = !Staff,
            };
        }
        void OnChanged() => PushSoon("upd", State);
        void OnData() { PeerDb.Reload(); _window.Dispatcher.BeginInvoke(() => PeersChanged?.Invoke()); }
        updater.Changed += OnChanged; updater.DataChanged += OnData;
        _cleanup.Add(() => { updater.Changed -= OnChanged; updater.DataChanged -= OnData; });
        updater.AutoCheck();

        Method("upd.state", _ => State());
        MethodAsync("upd.check", async _ => { await updater.CheckAsync().ConfigureAwait(true); return State(); });
        MethodAsync("upd.download", async _ => { if (!Staff) await updater.DownloadAsync().ConfigureAwait(true); return State(); });
        Method("upd.install", _ =>
        {
            if (Staff) return State();
            // The new release waits for this process to end before it replaces the files, then starts the app again.
            if (updater.Install()) _window.Dispatcher.BeginInvoke(Program.Shutdown);
            return State();
        });
    }
}
