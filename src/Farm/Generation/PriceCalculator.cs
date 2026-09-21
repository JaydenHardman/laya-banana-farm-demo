using BananaFarm.Contracts;

namespace BananaFarm.Farm.Generation;

/// <summary>Applies the pricing rules from SPEC 3.2.</summary>
public static class PriceCalculator
{
    /// <summary>Price band for a golden banana.</summary>
    public static readonly (decimal Min, decimal Max) GoldenBand = (30m, 70m);

    /// <summary>Price band for a banana past the over-ripe threshold.</summary>
    public static readonly (decimal Min, decimal Max) OverripeBand = (1m, 4m);

    /// <summary>Price band for an ordinary saleable banana.</summary>
    public static readonly (decimal Min, decimal Max) StandardBand = (5m, 10m);

    /// <summary>
    /// Price a banana. Rules are evaluated in order and the first match wins, so a banana that
    /// is both golden and over-ripe takes the golden band. It is still routed to
    /// <see cref="Topics.PastRipe"/> downstream — price and routing are governed
    /// independently (SPEC 3.2).
    /// </summary>
    public static decimal Calculate(
        BananaGrade grade,
        double ripeness,
        double overripeThreshold,
        IRandomSource random)
    {
        var band = grade switch
        {
            BananaGrade.Golden => GoldenBand,
            _ when ripeness > overripeThreshold => OverripeBand,
            _ => StandardBand,
        };

        var value = random.NextDouble((double)band.Min, (double)band.Max);
        return Math.Round((decimal)value, 2, MidpointRounding.AwayFromZero);
    }
}
