using Mazesta.Core.Health; using Microsoft.Extensions.Logging;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    /// <summary>The storage page's BitLocker box: each lettered volume's state, read when the page asks (WMI takes a moment, so off the UI thread).
    /// It never changes anything: turning BitLocker off is the user's own step, the page only says how.</summary>
    private void RegisterBitLocker()
    {
        MethodAsync("bitlocker.get", async _ =>
        {
            var volumes = await Task.Run(global::Mazesta.Hardware.Wmi.BitLockerReader.Read).ConfigureAwait(true);
            _log.LogInformation("BitLocker: {Volumes}", volumes is null ? "not readable" : string.Join(", ", volumes.Select(v => $"{v.Letter} {v.State} (protection {v.Protection?.ToString() ?? "-"}, conversion {v.Conversion?.ToString() ?? "-"}, protectors {v.Protectors?.ToString() ?? "-"}, recovery password {v.RecoveryPassword?.ToString() ?? "-"})")));
            return new { known = volumes is not null, volumes = (volumes ?? []).Select(v => new { letter = v.Letter, state = v.State.ToString(), canLock = v.CanLock, recovery = v.RecoveryPassword }) };
        });
    }
}
