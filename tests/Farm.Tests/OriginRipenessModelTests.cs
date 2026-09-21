using BananaFarm.Contracts;
using BananaFarm.Farm.Generation;

namespace BananaFarm.Farm.Tests;

public sealed class OriginRipenessModelTests
{
    [Fact]
    public void XFarm_never_produces_an_overripe_banana()
    {
        var model = new OriginRipenessModel(new FarmOptions());

        Assert.Equal(0, model.OverripeProbabilityGiven(FarmOrigin.XFarm));
    }

    [Fact]
    public void Conditional_probabilities_reproduce_the_global_overripe_rate()
    {
        var options = new FarmOptions();
        var model = new OriginRipenessModel(options);

        // Summing P(origin) x P(overripe | origin) must recover the global target, which is
        // the whole point of deriving the conditionals rather than hardcoding them.
        var recovered = Enum.GetValues<FarmOrigin>()
            .Sum(origin => model.ProbabilityOf(origin) * model.OverripeProbabilityGiven(origin));

        Assert.Equal(options.OverripeProbability, recovered, precision: 10);
    }

    [Theory]
    [InlineData(FarmOrigin.TheBananaBoyz, 0.2)]
    [InlineData(FarmOrigin.KindaCrazyNanas, 0.8)]
    public void Each_farm_supplies_its_configured_share_of_the_overripe_population(
        FarmOrigin origin,
        double expectedShare)
    {
        var options = new FarmOptions();
        var model = new OriginRipenessModel(options);

        var contribution = model.ProbabilityOf(origin) * model.OverripeProbabilityGiven(origin);
        var share = contribution / options.OverripeProbability;

        Assert.Equal(expectedShare, share, precision: 10);
    }

    [Fact]
    public void Conditional_probabilities_track_reconfigured_origin_weights()
    {
        // KindaCrazyNanas now produces a tenth of the bananas but still carries 80% of the
        // over-ripe pool, so its conditional probability must rise to compensate.
        var options = new FarmOptions
        {
            OriginWeights = new Dictionary<FarmOrigin, double>
            {
                [FarmOrigin.XFarm] = 0.45,
                [FarmOrigin.TheBananaBoyz] = 0.45,
                [FarmOrigin.KindaCrazyNanas] = 0.10,
            },
        };

        var model = new OriginRipenessModel(options);

        Assert.Equal(0.035 * 0.8 / 0.10, model.OverripeProbabilityGiven(FarmOrigin.KindaCrazyNanas), precision: 10);
        Assert.Equal(0.035 * 0.2 / 0.45, model.OverripeProbabilityGiven(FarmOrigin.TheBananaBoyz), precision: 10);
    }

    [Fact]
    public void Unsatisfiable_configuration_fails_fast_with_an_actionable_message()
    {
        // KindaCrazyNanas makes a tenth of all bananas but would have to supply 80% of a
        // population that is half over-ripe: it would need 400% of its output to be
        // over-ripe. TheBananaBoyz is comfortably satisfiable here, so the message must name
        // the farm that actually fails.
        var options = new FarmOptions
        {
            OverripeProbability = 0.5,
            OriginWeights = new Dictionary<FarmOrigin, double>
            {
                [FarmOrigin.XFarm] = 0.45,
                [FarmOrigin.TheBananaBoyz] = 0.45,
                [FarmOrigin.KindaCrazyNanas] = 0.10,
            },
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => new OriginRipenessModel(options));

        Assert.Contains("KindaCrazyNanas", exception.Message, StringComparison.Ordinal);
        Assert.Contains("unsatisfiable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_farm_that_produces_nothing_cannot_own_part_of_the_overripe_population()
    {
        var options = new FarmOptions
        {
            OriginWeights = new Dictionary<FarmOrigin, double>
            {
                [FarmOrigin.XFarm] = 1,
                [FarmOrigin.TheBananaBoyz] = 0,
                [FarmOrigin.KindaCrazyNanas] = 1,
            },
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => new OriginRipenessModel(options));

        Assert.Contains("TheBananaBoyz", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sampling_reproduces_the_configured_origin_weights()
    {
        var options = new FarmOptions();
        var model = new OriginRipenessModel(options);
        var random = new SystemRandomSource(new Random(20260921));

        var counts = new Dictionary<FarmOrigin, int>();

        for (var i = 0; i < 120_000; i++)
        {
            var origin = model.SampleOrigin(random);
            counts[origin] = counts.GetValueOrDefault(origin) + 1;
        }

        foreach (var origin in Enum.GetValues<FarmOrigin>())
        {
            var observed = counts.GetValueOrDefault(origin) / 120_000.0;
            Assert.InRange(observed, model.ProbabilityOf(origin) - 0.01, model.ProbabilityOf(origin) + 0.01);
        }
    }
}
