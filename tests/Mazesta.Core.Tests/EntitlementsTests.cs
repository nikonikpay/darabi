using Xunit;
using Mazesta.Core.Access;
namespace Mazesta.Core.Tests;

public class EntitlementsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact] public void A_copy_the_site_has_not_locked_is_free_and_everything_is_open()
    {
        foreach (var json in new[] { null, "", "not json", "[]", "{}", """{"v":1,"enforce":false,"locks":["page:lights"]}""" })
            Assert.False(Entitlements.Parse(json).IsLocked("lights", "Nav_Group_Lights", Now), json);
    }

    [Fact] public void A_page_and_a_menu_can_be_locked_but_the_open_pages_never()
    {
        var e = Entitlements.Parse("""{"v":1,"enforce":true,"plan":"free","locks":["page:lights","menu:Nav_Group_Tests","page:settings","menu:Nav_Messages"]}""");
        Assert.True(e.IsLocked("lights", "Nav_Group_Lights", Now));
        Assert.False(e.IsLocked("fans", "Nav_Group_Lights", Now));
        Assert.True(e.IsLocked("benchmarks", "Nav_Group_Tests", Now));   // the whole menu
        Assert.False(e.IsLocked("settings", "Nav_Settings", Now)); Assert.False(e.IsLocked("messages", "Nav_Messages", Now));
    }

    [Fact] public void A_pro_account_has_no_locks_until_its_plan_runs_out_while_offline()
    {
        var e = Entitlements.Parse("""{"enforce":true,"plan":"pro","until":"2026-11-01T00:00:00Z","account":"0912***567","locks":[],"freeLocks":["page:lights"]}""");
        Assert.False(e.IsLocked("lights", "Nav_Group_Lights", Now));
        Assert.True(e.IsLocked("lights", "Nav_Group_Lights", new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero)));
    }
}
