using Xunit; using Mazesta.Core.Health;
namespace Mazesta.Core.Tests;

public class InternetQualityTests
{
    [Fact] public void A_fast_quiet_line_is_great_for_all() => Assert.Equal(new InternetScores(5, 5, 5), InternetQuality.Score(159, 15, 2, 0, 30));
    // The owner's speed.cloudflare.com run (159 Mbps, 111 ms, jitter 43 ms, no loss) was graded 4, 3 and 3 stars there; the same thresholds agree.
    [Fact] public void Matches_cloudflares_own_grading() => Assert.Equal(new InternetScores(4, 3, 3), InternetQuality.Score(159, 111, 43, 0, 111));
    [Fact] public void Without_a_loaded_latency_nothing_is_graded() => Assert.Equal(new InternetScores(null, null, null), InternetQuality.Score(159, 15, 2, 0, null));
}
