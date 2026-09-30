using Xunit;
namespace Mazesta.Diagnostics.Tests;

public class SessionBreakTests
{
    private static readonly DateTimeOffset Saved = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BootBefore = Saved.AddHours(-3), BootAfter = Saved.AddMinutes(2);
    private static BreakEvent E(string provider, int id, int minutes = 1, string? text = null) => new(Saved.AddMinutes(minutes), provider, id, text);

    [Fact] public void A_bug_check_is_a_blue_screen_with_its_stop_code()
    {
        var r = SessionBreak.Classify(Saved, BootAfter, [E("Microsoft-Windows-Kernel-Power", 41, 2), E("Microsoft-Windows-WER-SystemErrorReporting", 1001, 3,
            "The computer has rebooted from a bugcheck.  The bugcheck was: 0x00000124 (0x0000000000000000, 0xffff9c0b1a2e4028, 0x00000000be000000, 0x00000000800400)")]);
        Assert.Equal((BreakCause.BlueScreen, "0x00000124"), (r.Cause, r.Code));
    }

    [Fact] public void An_unexpected_restart_without_a_bug_check_is_a_power_loss_or_forced_reset()
        => Assert.Equal(BreakCause.PowerLoss, SessionBreak.Classify(Saved, BootAfter, [E("Microsoft-Windows-Kernel-Power", 41, 2)]).Cause);

    [Fact] public void A_clean_restart_after_the_checkpoint_is_a_restart()
        => Assert.Equal(BreakCause.Restarted, SessionBreak.Classify(Saved, BootAfter, []).Cause);

    [Fact] public void A_crash_record_naming_the_app_is_an_app_crash_and_one_naming_another_program_is_not()
    {
        Assert.Equal(BreakCause.AppCrashed, SessionBreak.Classify(Saved, BootBefore, [E("Application Error", 1000, 1, "Faulting application name: MazestaWeb.exe, version: 0.9")]).Cause);
        Assert.Equal(BreakCause.AppClosed, SessionBreak.Classify(Saved, BootBefore, [E("Application Error", 1000, 1, "Faulting application name: chrome.exe")]).Cause);
    }

    [Fact] public void Events_from_before_the_checkpoint_do_not_count_and_display_resets_are_counted_beside()
    {
        var r = SessionBreak.Classify(Saved, BootBefore, [E("Microsoft-Windows-Kernel-Power", 41, -60), E("Display", 4101, 1), E("Display", 4101, 2)]);
        Assert.Equal((BreakCause.AppClosed, 2), (r.Cause, r.DisplayResets));
    }
}
