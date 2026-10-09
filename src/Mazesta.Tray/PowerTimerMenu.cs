using System.Diagnostics; using Mazesta.Core.Tray;
namespace Mazesta.Tray;

/// <summary>
/// "Sleep or shut down the computer in so long", in the tray's menu: for each of the two a few ready waits and one the technician types (minutes, or hours:minutes).
/// One at a time; setting another replaces it, and the menu shows how long is left and cancels it. A minute before it happens a notification says so. It is kept in
/// memory only: if the tray ends, so does the plan (a shutdown nobody can see coming must never survive a restart). One timer, set to the moment it is next needed.
/// </summary>
internal sealed class PowerTimerMenu : IDisposable
{
    private static readonly int[] Presets = [15, 30, 60, 120, 180, 300];
    private readonly Action<string, ToolTipIcon> _notify; private readonly System.Windows.Forms.Timer _timer = new();
    private PowerSchedule? _plan; private bool _warned;
    public ToolStripMenuItem Menu { get; }

    public PowerTimerMenu(Action<string, ToolTipIcon> notify)
    {
        _notify = notify; _timer.Tick += (_, _) => Tick();
        Menu = new ToolStripMenuItem(TrayText.PowerTimer);
        Menu.DropDownOpening += (_, _) => Build();
        Build();
    }

    private void Build()
    {
        Menu.DropDownItems.Clear();
        if (_plan is { } plan)
        {
            Menu.DropDownItems.Add(new ToolStripMenuItem(TrayText.PowerPlanned(plan.Action, PowerSchedule.Format(plan.Left(DateTimeOffset.Now)))) { Enabled = false });
            Menu.DropDownItems.Add(new ToolStripMenuItem(TrayText.PowerCancel, null, (_, _) => Cancel()));
            Menu.DropDownItems.Add(new ToolStripSeparator());
        }
        foreach (var action in new[] { PowerAction.Sleep, PowerAction.Shutdown })
        {
            var sub = new ToolStripMenuItem(TrayText.PowerAfter(action));
            foreach (int minutes in Presets) sub.DropDownItems.Add(new ToolStripMenuItem(TrayText.PowerWait(minutes), null, (_, _) => Set(action, TimeSpan.FromMinutes(minutes))));
            sub.DropDownItems.Add(new ToolStripSeparator());
            sub.DropDownItems.Add(new ToolStripMenuItem(TrayText.PowerCustom, null, (_, _) => Ask(action)));
            Menu.DropDownItems.Add(sub);
        }
    }

    private void Ask(PowerAction action)
    {
        using var form = new Form { Text = TrayText.PowerAfter(action), RightToLeft = RightToLeft.Yes, RightToLeftLayout = true, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(380, 150), TopMost = true };
        var label = new Label { Text = TrayText.PowerCustomHelp, Left = 14, Top = 14, Width = 352, Height = 44 };
        var box = new TextBox { Left = 14, Top = 62, Width = 352, TextAlign = HorizontalAlignment.Left, RightToLeft = RightToLeft.No, Text = "60" };
        var ok = new Button { Text = TrayText.PowerSet, DialogResult = DialogResult.OK, Left = 14, Top = 104, Width = 110 };
        var cancel = new Button { Text = TrayText.PowerNo, DialogResult = DialogResult.Cancel, Left = 132, Top = 104, Width = 110 };
        form.Controls.AddRange([label, box, ok, cancel]); form.AcceptButton = ok; form.CancelButton = cancel;
        while (form.ShowDialog() == DialogResult.OK)
        {
            if (PowerSchedule.ParseWait(box.Text) is { } wait) { Set(action, wait); return; }
            MessageBox.Show(TrayText.PowerBadWait, TrayText.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        }
    }

    private void Set(PowerAction action, TimeSpan wait)
    {
        _plan = new(action, DateTimeOffset.Now + wait); _warned = false;
        Arm(); _notify(TrayText.PowerSetNote(action, PowerSchedule.Format(wait)), ToolTipIcon.Info);
    }

    private void Cancel() { _timer.Stop(); _plan = null; _notify(TrayText.PowerCancelled, ToolTipIcon.Info); }

    /// <summary>The timer waits for the warning, then for the moment itself: nothing wakes in between.</summary>
    private void Arm()
    {
        if (_plan is not { } p) return;
        var now = DateTimeOffset.Now; var next = !_warned && p.Left(now) > PowerSchedule.Warning ? p.Left(now) - PowerSchedule.Warning : p.Left(now);
        _timer.Stop(); _timer.Interval = (int)Math.Clamp(next.TotalMilliseconds, 1000, int.MaxValue); _timer.Start();
    }

    private void Tick()
    {
        _timer.Stop();
        if (_plan is not { } p) return;
        var now = DateTimeOffset.Now;
        if (p.IsDue(now) || (_warned && p.Left(now) <= TimeSpan.FromSeconds(2))) { _plan = null; Run(p.Action); return; }
        if (!_warned) { _warned = true; _notify(TrayText.PowerSoon(p.Action), ToolTipIcon.Warning); }
        Arm();
    }

    private void Run(PowerAction action)
    {
        try
        {
            if (action == PowerAction.Sleep) Application.SetSuspendState(PowerState.Suspend, false, false);
            else Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 0") { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { _notify(TrayText.PowerFailed(e.Message), ToolTipIcon.Error); }
    }

    public void Dispose() => _timer.Dispose();
}
