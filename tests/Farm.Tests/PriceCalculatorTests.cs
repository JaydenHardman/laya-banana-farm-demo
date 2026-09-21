using BananaFarm.Contracts;
using BananaFarm.Farm.Generation;

namespace BananaFarm.Farm.Tests;

public sealed class PriceCalculatorTests
{
    private static readonly IRandomSource Random = new SystemRandomSource(new Random(7));

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    public void An_ordinary_banana_is_priced_between_five_and_ten(double ripeness)
    {
        var price = PriceCalculator.Calculate(BananaGrade.B, ripeness, 0.75, Random);

        Assert.InRange(price, 5m, 10m);
    }

    [Theory]
    [InlineData(0.751)]
    [InlineData(0.9)]
    [InlineData(1.0)]
    public void An_overripe_banana_is_priced_between_one_and_four(double ripeness)
    {
        var price = PriceCalculator.Calculate(BananaGrade.C, ripeness, 0.75, Random);

        Assert.InRange(price, 1m, 4m);
    }

    [Fact]
    public void A_golden_banana_is_priced_between_thirty_and_seventy()
    {
        var price = PriceCalculator.Calculate(BananaGrade.Golden, 0.4, 0.75, Random);

        Assert.InRange(price, 30m, 70m);
    }

    [Fact]
    public void Golden_takes_precedence_over_overripe_when_pricing()
    {
        // A banana can be both golden and past ripe. Pricing follows the golden band; routing
        // to pastRipe is decided separately in the factory.
        var price = PriceCalculator.Calculate(BananaGrade.Golden, 0.95, 0.75, Random);

        Assert.InRange(price, 30m, 70m);
    }

    [Fact]
    public void Prices_are_rounded_to_two_decimal_places()
    {
        for (var i = 0; i < 1_000; i++)
        {
            var price = PriceCalculator.Calculate(BananaGrade.A, 0.3, 0.75, Random);

            Assert.Equal(price, Math.Round(price, 2));
        }
    }
}
