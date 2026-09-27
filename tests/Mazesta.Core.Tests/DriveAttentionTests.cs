using Xunit; using Mazesta.Core.Health; using Mazesta.Core.Providers;
namespace Mazesta.Core.Tests;

public class DriveAttentionTests
{
    private static DriveHealth Drive(string? status, long? readErrors = null, long? writeErrors = null) => new("SSD", null, status, 3, 40, null, readErrors, writeErrors, 1000);

    [Theory, InlineData("Warning"), InlineData("Unhealthy")] public void A_drive_windows_flags_needs_attention(string status) => Assert.True(DriveAttention.Needs(Drive(status)));
    [Fact] public void Uncorrected_errors_need_attention_even_when_windows_says_healthy()
    {
        Assert.True(DriveAttention.Needs(Drive("Healthy", readErrors: 1))); Assert.True(DriveAttention.Needs(Drive("Healthy", writeErrors: 2)));
    }
    [Fact] public void A_healthy_drive_or_one_with_nothing_reported_does_not()
    {
        Assert.False(DriveAttention.Needs(Drive("Healthy", 0, 0))); Assert.False(DriveAttention.Needs(Drive(null)));
    }
}
