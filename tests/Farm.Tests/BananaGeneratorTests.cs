using BananaFarm.Contracts;
using BananaFarm.Farm.Generation;
using Microsoft.Extensions.Options;

namespace BananaFarm.Farm.Tests;

/// <summary>
/// Statistical assertions over the generated population. The random source is seeded, so
/// these are deterministic rather than flaky, and the tolerances are wide enough that a
/// different seed would still pass but a broken distribution would not.
/// </summary>
public sealed class BananaGeneratorTests
{
    private const int SampleSize = 200_000;

    private static readonly FarmOptions Options = new();

    private static IReadOnlyList<Banana> GenerateSample(int seed = 20260921)
    {
        var options = Options;

        var generator = new BananaGenerator(
            Microsoft.Extensions.Options.Options.Create(options),
            new OriginRipenessModel(options),
            new SystemRandomSource(new Random(seed)),
            TimeProvider.System);

        return generator.Next(SampleSize);
    }

    [Fact]
    public void Three_and_a_half_percent_of_bananas_are_overripe()
    {
        var bananas = GenerateSample();

        var overripeRate = bananas.Count(banana => banana.Ripeness > 0.75f) / (double)SampleSize;

        Assert.InRange(overripeRate, 0.032, 0.038);
    }

    [Fact]
    public void XFarm_never_produces_an_overripe_banana()
    {
        var bananas = GenerateSample();

        Assert.DoesNotContain(
            bananas,
            banana => banana.FarmOrigin == FarmOrigin.XFarm && banana.Ripeness > 0.75f);
    }

    [Fact]
    public void TheBananaBoyz_produce_a_fifth_of_the_overripe_population()
    {
        var overripe = GenerateSample().Where(banana => banana.Ripeness > 0.75f).ToList();

        var share = overripe.Count(banana => banana.FarmOrigin == FarmOrigin.TheBananaBoyz)
            / (double)overripe.Count;

        Assert.InRange(share, 0.17, 0.23);
    }

    [Fact]
    public void KindaCrazyNanas_produce_the_rest_of_the_overripe_population()
    {
        var overripe = GenerateSample().Where(banana => banana.Ripeness > 0.75f).ToList();

        var share = overripe.Count(banana => banana.FarmOrigin == FarmOrigin.KindaCrazyNanas)
            / (double)overripe.Count;

        Assert.InRange(share, 0.77, 0.83);
    }

    [Fact]
    public void Roughly_one_banana_in_a_thousand_is_golden()
    {
        var bananas = GenerateSample();

        var goldenRate = bananas.Count(banana => banana.Grade == BananaGrade.Golden)
            / (double)SampleSize;

        Assert.InRange(goldenRate, 0.0006, 0.0015);
    }

    [Fact]
    public void Every_banana_is_priced_within_the_band_its_rules_select()
    {
        foreach (var banana in GenerateSample())
        {
            var (min, max) = banana switch
            {
                { Grade: BananaGrade.Golden } => PriceCalculator.GoldenBand,
                { Ripeness: > 0.75f } => PriceCalculator.OverripeBand,
                _ => PriceCalculator.StandardBand,
            };

            Assert.InRange(banana.Price, min, max);
        }
    }

    [Fact]
    public void Ripeness_always_falls_within_the_unit_interval()
    {
        Assert.All(GenerateSample(), banana => Assert.InRange(banana.Ripeness, 0f, 1f));
    }

    [Fact]
    public void Weight_is_clamped_to_the_configured_bounds()
    {
        Assert.All(
            GenerateSample(),
            banana => Assert.InRange(
                banana.Weight,
                (float)Options.WeightMinGrams,
                (float)Options.WeightMaxGrams));
    }

    [Fact]
    public void Non_golden_grades_follow_their_configured_weights()
    {
        var bananas = GenerateSample().Where(banana => banana.Grade != BananaGrade.Golden).ToList();

        var gradeA = bananas.Count(banana => banana.Grade == BananaGrade.A) / (double)bananas.Count;
        var gradeB = bananas.Count(banana => banana.Grade == BananaGrade.B) / (double)bananas.Count;
        var gradeC = bananas.Count(banana => banana.Grade == BananaGrade.C) / (double)bananas.Count;

        Assert.InRange(gradeA, 0.29, 0.31);
        Assert.InRange(gradeB, 0.44, 0.46);
        Assert.InRange(gradeC, 0.24, 0.26);
    }

    [Fact]
    public void Ordinary_bananas_cluster_around_ideal_ripeness()
    {
        // The triangular distribution should put its mass near 0.5, not spread it flat.
        var ordinary = GenerateSample().Where(banana => banana.Ripeness <= 0.75f).ToList();

        var nearIdeal = ordinary.Count(banana => banana.Ripeness is > 0.35f and < 0.65f)
            / (double)ordinary.Count;

        Assert.True(
            nearIdeal > 0.4,
            $"Expected ordinary bananas to cluster near ideal ripeness, but only {nearIdeal:P1} " +
            "landed between 0.35 and 0.65.");
    }

    [Fact]
    public void Each_banana_gets_its_own_identity()
    {
        var bananas = GenerateSample().Take(10_000).ToList();

        Assert.Equal(bananas.Count, bananas.Select(banana => banana.Id).Distinct().Count());
    }
}
