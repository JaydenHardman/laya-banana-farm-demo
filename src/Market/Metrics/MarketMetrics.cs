using BananaFarm.Contracts;

namespace BananaFarm.Market.Metrics;

/// <summary>Names of the raw accumulators the market maintains.</summary>
/// <remarks>
/// Raw counters and running sums are stored rather than individual events, so a SQL-backed
/// implementation is a direct translation and the store never grows with throughput.
/// </remarks>
public static class MarketMetrics
{
    /// <summary>Sum and count of filled box values.</summary>
    public const string BoxPrice = "box.price";

    /// <summary>Sum and count of prices lost to past-due bananas.</summary>
    public const string PastRipeLoss = "pastripe.loss";

    /// <summary>Sum and count of golden banana values.</summary>
    public const string GoldenBananas = "golden.count";

    /// <summary>Sum and count of bananas released from boxes that never filled.</summary>
    public const string TimedOutBananas = "toolong.count";
}

/// <summary>A running sum and the number of observations that produced it.</summary>
public readonly record struct MetricAggregate(double Sum, long Count)
{
    /// <summary>Mean observation, or zero when nothing has been recorded.</summary>
    public double Mean => Count == 0 ? 0 : Sum / Count;

    /// <summary>An aggregate with no observations.</summary>
    public static MetricAggregate Empty => new(0, 0);
}

/// <summary>
/// Records market events and reads back their aggregates.
/// </summary>
public interface IMarketMetricsStore
{
    /// <summary>Add one observation to the global accumulator for <paramref name="metric"/>.</summary>
    Task RecordGlobalAsync(string metric, double value, CancellationToken cancellationToken);

    /// <summary>
    /// Add one observation to <paramref name="metric"/> for a single farm.
    /// </summary>
    /// <remarks>
    /// Kept separate from <see cref="RecordGlobalAsync"/> because a box holds bananas from
    /// several farms: it counts once globally, but contributes one observation per
    /// contributing farm, each carrying that farm's share of the total.
    /// </remarks>
    Task RecordFarmAsync(
        string metric,
        FarmOrigin origin,
        double value,
        CancellationToken cancellationToken);

    /// <summary>Read the global aggregate for <paramref name="metric"/>.</summary>
    Task<MetricAggregate> GetAsync(string metric, CancellationToken cancellationToken);

    /// <summary>Read <paramref name="metric"/> broken down by farm.</summary>
    Task<IReadOnlyDictionary<FarmOrigin, MetricAggregate>> GetByFarmAsync(
        string metric,
        CancellationToken cancellationToken);
}

/// <summary>Convenience helpers over <see cref="IMarketMetricsStore"/>.</summary>
public static class MarketMetricsStoreExtensions
{
    /// <summary>
    /// Record one observation attributed to a single farm, both globally and against that
    /// farm. The common case: a banana belongs to exactly one farm.
    /// </summary>
    /// <remarks>
    /// An extension rather than a member of the interface, so that implementations stay
    /// minimal and callers reach it through any concrete type.
    /// </remarks>
    public static async Task RecordAsync(
        this IMarketMetricsStore store,
        string metric,
        FarmOrigin origin,
        double value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);

        await store.RecordGlobalAsync(metric, value, cancellationToken).ConfigureAwait(false);
        await store.RecordFarmAsync(metric, origin, value, cancellationToken).ConfigureAwait(false);
    }
}
