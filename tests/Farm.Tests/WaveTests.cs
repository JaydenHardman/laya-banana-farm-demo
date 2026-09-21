using BananaFarm.Farm.Generation;
using BananaFarm.Farm.Waves;
using Microsoft.Extensions.Options;

namespace BananaFarm.Farm.Tests;

public sealed class PoissonSamplerTests
{
    [Theory]
    [InlineData(0.5)]
    [InlineData(10)]
    [InlineData(26)]
    public void Sample_mean_converges_on_the_requested_mean(double mean)
    {
        var random = new SystemRandomSource(new Random(4242));

        var total = 0L;
        const int Draws = 50_000;

        for (var i = 0; i < Draws; i++)
        {
            total += PoissonSampler.Sample(mean, random);
        }

        Assert.InRange(total / (double)Draws, mean * 0.97, mean * 1.03);
    }

    [Fact]
    public void A_non_positive_mean_yields_nothing()
    {
        var random = new SystemRandomSource(new Random(1));

        Assert.Equal(0, PoissonSampler.Sample(0, random));
        Assert.Equal(0, PoissonSampler.Sample(-5, random));
    }

    [Fact]
    public void Counts_vary_rather_than_repeating_a_fixed_value()
    {
        var random = new SystemRandomSource(new Random(99));

        var distinct = Enumerable.Range(0, 500)
            .Select(_ => PoissonSampler.Sample(10, random))
            .Distinct()
            .Count();

        Assert.True(distinct > 5, $"Expected varied counts, saw only {distinct} distinct values.");
    }
}

public sealed class WaveRateControllerTests
{
    private static WaveRateController CreateController(int seed = 1234) =>
        new(Options.Create(new WaveOptions()), new SystemRandomSource(new Random(seed)));

    [Fact]
    public void The_multiplier_stays_within_its_configured_clamps()
    {
        var options = new WaveOptions();
        var controller = CreateController();

        for (var i = 0; i < 20_000; i++)
        {
            controller.Advance(TimeSpan.FromMilliseconds(50), 200);

            Assert.InRange(controller.Multiplier, options.MinMultiplier, options.MaxMultiplier);
        }
    }

    [Fact]
    public void Production_visits_downturns_and_spikes_rather_than_holding_steady()
    {
        var controller = CreateController();
        var seen = new HashSet<ProductionRegime>();

        for (var i = 0; i < 20_000; i++)
        {
            controller.Advance(TimeSpan.FromMilliseconds(50), 200);
            seen.Add(controller.Regime);
        }

        Assert.Contains(ProductionRegime.Spike, seen);
        Assert.Contains(ProductionRegime.Downturn, seen);
        Assert.Contains(ProductionRegime.Normal, seen);
    }

    [Fact]
    public void The_long_run_average_rate_stays_in_the_neighbourhood_of_the_base_rate()
    {
        // Spikes overshoot and downturns undershoot, but the chain spends most of its time in
        // Normal, so the mean should land within a reasonable band of the base rate rather
        // than drifting away from it.
        var controller = CreateController();

        var total = 0d;
        const int Ticks = 40_000;

        for (var i = 0; i < Ticks; i++)
        {
            total += controller.Advance(TimeSpan.FromMilliseconds(50), 200);
        }

        Assert.InRange(total / Ticks, 120, 320);
    }

    [Fact]
    public void A_zero_length_step_does_not_move_the_simulation()
    {
        var controller = CreateController();
        var before = controller.Multiplier;

        var rate = controller.Advance(TimeSpan.Zero, 200);

        Assert.Equal(before, controller.Multiplier);
        Assert.Equal(200 * before, rate);
    }
}
