using Xunit; using Mazesta.Core.Windows;
namespace Mazesta.Core.Tests;

public class FileToolsTests
{
    [Theory, InlineData(1, FileLockKind.Window), InlineData(2, FileLockKind.Window), InlineData(3, FileLockKind.Service), InlineData(4, FileLockKind.Explorer), InlineData(5, FileLockKind.Console), InlineData(1000, FileLockKind.Critical), InlineData(0, FileLockKind.Other)]
    public void Restart_managers_app_types_map_to_kinds(int type, FileLockKind kind) => Assert.Equal(kind, FileLockHolder.KindOf(type));

    [Fact] public void A_service_a_critical_process_or_the_system_is_never_closed()
    {
        Assert.False(new FileLockHolder(900, 1, "svc", null, FileLockKind.Service).CanClose);
        Assert.False(new FileLockHolder(900, 1, "csrss", null, FileLockKind.Critical).CanClose);
        Assert.False(new FileLockHolder(4, 1, "System", null, FileLockKind.Other).CanClose);
        Assert.True(new FileLockHolder(900, 1, "Word", null, FileLockKind.Window).CanClose);
    }
}
