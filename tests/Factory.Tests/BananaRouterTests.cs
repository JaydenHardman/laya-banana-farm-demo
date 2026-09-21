using BananaFarm.Contracts;
using BananaFarm.Factory.Routing;

namespace BananaFarm.Factory.Tests;

public sealed class BananaRouterTests
{
    [Fact]
    public void An_ordinary_banana_goes_into_a_box()
    {
        Assert.Equal(BananaDestination.Box, BananaRouter.Decide(TestData.Ordinary));
    }

    [Fact]
    public void A_golden_banana_is_published_on_its_own()
    {
        Assert.Equal(BananaDestination.GoldenBanana, BananaRouter.Decide(TestData.Golden));
    }

    [Fact]
    public void A_banana_marked_for_disposal_goes_to_past_ripe()
    {
        Assert.Equal(BananaDestination.PastRipe, BananaRouter.Decide(TestData.Rotten));
    }

    [Fact]
    public void Disposal_beats_golden()
    {
        // Explicit in the brief: a golden banana that should be thrown away is published to
        // pastRipe, not goldenBanana. This ordering is the single most load-bearing detail in
        // the routing rules.
        Assert.Equal(BananaDestination.PastRipe, BananaRouter.Decide(TestData.GoldenAndRotten));
    }

    [Theory]
    [InlineData(0.49, BananaDestination.Box)]
    [InlineData(0.50, BananaDestination.PastRipe)]
    [InlineData(0.51, BananaDestination.PastRipe)]
    public void The_disposal_threshold_is_applied_at_one_half(
        double throwAway,
        BananaDestination expected)
    {
        var classification = new BananaClassification(ClassifiedGrade.B, 0.9, 2, throwAway);

        Assert.Equal(expected, BananaRouter.Decide(classification));
    }

    [Fact]
    public void Each_destination_maps_to_its_topic_and_reason()
    {
        Assert.Equal(Topics.PastRipe, BananaRouter.TopicFor(BananaDestination.PastRipe));
        Assert.Equal(Topics.GoldenBanana, BananaRouter.TopicFor(BananaDestination.GoldenBanana));
        Assert.Equal(Topics.FilledBox, BananaRouter.TopicFor(BananaDestination.Box));

        Assert.Equal(PublishReason.ThrownAway, BananaRouter.ReasonFor(BananaDestination.PastRipe));
        Assert.Equal(PublishReason.Golden, BananaRouter.ReasonFor(BananaDestination.GoldenBanana));
        Assert.Equal(PublishReason.Boxed, BananaRouter.ReasonFor(BananaDestination.Box));
    }
}
