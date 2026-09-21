using BananaFarm.Farm.Generation;

namespace BananaFarm.Farm.Waves;

/// <summary>Draws counts from a Poisson distribution.</summary>
/// <remarks>
/// Emitting exactly <c>rate x dt</c> bananas per tick would produce suspiciously even
/// output. Drawing the per-tick count from a Poisson distribution gives the irregular
/// arrivals of a real process while preserving the target mean.
/// </remarks>
public static class PoissonSampler
{
    /// <summary>
    /// Knuth's method. Linear in the returned value, which is fine for the small means this
    /// system uses (a 50ms tick at 200/s averages 10), and it degrades gracefully during
    /// spikes rather than becoming inaccurate.
    /// </summary>
    public static int Sample(double mean, IRandomSource random)
    {
        if (mean <= 0)
        {
            return 0;
        }

        // Guard against a misconfigured rate turning one tick into an unbounded loop.
        const double MaxMean = 10_000;
        var limit = Math.Min(mean, MaxMean);

        var target = Math.Exp(-limit);
        var product = 1.0;
        var count = 0;

        do
        {
            count++;
            product *= random.NextDouble();
        }
        while (product > target);

        return count - 1;
    }
}
