using System.Globalization;
using BananaFarm.Contracts;

namespace BananaFarm.Factory.Classification;

/// <summary>
/// Derives the cache key that decides which bananas share a classification.
/// </summary>
/// <remarks>
/// This is the deliberate accuracy-for-throughput trade described in SPEC 6.1. The model
/// costs 300-450ms per banana on CPU, which cannot sustain 200/s. Quantising the continuous
/// fields collapses the input space to a few thousand buckets, so after warm-up the model
/// sees only genuinely novel shapes. Two bananas in one bucket receive the same verdict.
/// </remarks>
public static class ClassificationBucket
{
    /// <summary>Build the bucket key for <paramref name="banana"/>.</summary>
    public static string KeyFor(Banana banana, FactoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(banana);
        ArgumentNullException.ThrowIfNull(options);

        var ripenessBucket = Quantise(banana.Ripeness, options.CacheRipenessBucket);
        var weightBucket = Quantise(banana.Weight, options.CacheWeightBucketGrams);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{banana.FarmOrigin}|{banana.Grade}|{ripenessBucket}|{weightBucket}");
    }

    /// <summary>
    /// Floor <paramref name="value"/> onto a grid of <paramref name="bucketSize"/>, returning
    /// the bucket index rather than the value so the key stays short and exact.
    /// </summary>
    private static long Quantise(double value, double bucketSize) =>
        bucketSize <= 0 ? 0 : (long)Math.Floor(value / bucketSize);
}
