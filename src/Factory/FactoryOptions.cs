using System.ComponentModel.DataAnnotations;

namespace BananaFarm.Factory;

/// <summary>Tuning for classification throughput and boxing behaviour.</summary>
public sealed class FactoryOptions
{
    public const string SectionName = "Factory";

    /// <summary>
    /// Where open boxes live: <c>redis</c> (default, coordinates across replicas and survives
    /// a restart) or <c>memory</c> (single instance, no external dependency).
    /// </summary>
    [RegularExpression("^(redis|memory)$", ErrorMessage = "BoxStore must be 'redis' or 'memory'.")]
    public string BoxStore { get; set; } = "redis";

    /// <summary>Bananas per box before it is published as filled.</summary>
    [Range(1, 10_000)]
    public int BoxCapacity { get; set; } = 20;

    /// <summary>
    /// Seconds a box may wait, measured from its first banana, before its contents are
    /// flushed to <c>tooLongToFill</c>.
    /// </summary>
    [Range(1, 3_600)]
    public int BoxTimeoutSeconds { get; set; } = 10;

    /// <summary>How often expired boxes are swept, in milliseconds.</summary>
    [Range(50, 60_000)]
    public int SweepIntervalMilliseconds { get; set; } = 500;

    /// <summary>Simultaneous in-flight classification batches.</summary>
    [Range(1, 256)]
    public int ClassifierConcurrency { get; set; } = 5;

    /// <summary>Largest batch sent to the classification service in one request.</summary>
    [Range(1, 1_000)]
    public int ClassifierBatchSize { get; set; } = 32;

    /// <summary>
    /// How long a partial batch waits for more cache misses before being dispatched, in
    /// milliseconds. Trades a little latency for far fewer HTTP round-trips.
    /// </summary>
    [Range(1, 10_000)]
    public int ClassifierBatchDelayMilliseconds { get; set; } = 25;

    /// <summary>
    /// Ripeness bucket width for the classification cache. Bananas whose ripeness falls in
    /// the same bucket share a classification, which is what makes 200/s affordable
    /// (SPEC 6.1). Narrower is more accurate and more expensive.
    /// </summary>
    [Range(0.001, 1)]
    public double CacheRipenessBucket { get; set; } = 0.05;

    /// <summary>Weight bucket width in grams for the classification cache.</summary>
    [Range(0.1, 1_000)]
    public double CacheWeightBucketGrams { get; set; } = 10;

    /// <summary>How long a cached classification stays valid, in seconds.</summary>
    [Range(1, 86_400)]
    public int CacheTtlSeconds { get; set; } = 3_600;
}
