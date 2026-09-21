using BananaFarm.Contracts;
using BananaFarm.Market.Metrics;

namespace BananaFarm.Market.Stats;

/// <summary>How a stat reduces its underlying aggregate to a single number.</summary>
public enum StatAggregation
{
    /// <summary>Running total of observed values.</summary>
    Total,

    /// <summary>Mean observed value.</summary>
    Mean,

    /// <summary>Number of observations, ignoring their values.</summary>
    Count,
}

/// <summary>
/// Base class for a stat backed by one accumulator in <see cref="IMarketMetricsStore"/>.
/// </summary>
/// <remarks>
/// Subclasses supply identity and a reduction, which is why adding a metric is a handful of
/// lines rather than a new read path.
/// </remarks>
public abstract class MetricStat : IStat
{
    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Unit { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <summary>The accumulator this stat reads.</summary>
    protected abstract string Metric { get; }

    /// <summary>How the accumulator is reduced to a value.</summary>
    protected abstract StatAggregation Aggregation { get; }

    /// <inheritdoc />
    public async Task<StatValue> ComputeAsync(
        IMarketMetricsStore store,
        CancellationToken cancellationToken)
    {
        var aggregate = await store.GetAsync(Metric, cancellationToken).ConfigureAwait(false);
        return ToValue(aggregate);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<FarmOrigin, StatValue>> ComputeByFarmAsync(
        IMarketMetricsStore store,
        CancellationToken cancellationToken)
    {
        var byFarm = await store.GetByFarmAsync(Metric, cancellationToken).ConfigureAwait(false);
        return byFarm.ToDictionary(pair => pair.Key, pair => ToValue(pair.Value));
    }

    /// <summary>Reduce an aggregate according to <see cref="Aggregation"/>.</summary>
    public StatValue ToValue(MetricAggregate aggregate)
    {
        var value = Aggregation switch
        {
            StatAggregation.Total => aggregate.Sum,
            StatAggregation.Mean => aggregate.Mean,
            StatAggregation.Count => aggregate.Count,
            _ => throw new InvalidOperationException($"Unhandled aggregation {Aggregation}."),
        };

        return new StatValue(
            Id: Id,
            Value: Math.Round(value, 4, MidpointRounding.AwayFromZero),
            Unit: Unit,
            AsOf: DateTimeOffset.UtcNow,
            SampleCount: aggregate.Count);
    }
}
