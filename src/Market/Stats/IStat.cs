using BananaFarm.Contracts;
using BananaFarm.Market.Metrics;

namespace BananaFarm.Market.Stats;

/// <summary>A single value the market reports.</summary>
/// <param name="Id">Stable identifier used in routes and subscriptions.</param>
/// <param name="Value">The current value.</param>
/// <param name="Unit">What the value is measured in.</param>
/// <param name="AsOf">When it was computed.</param>
/// <param name="SampleCount">Observations behind the value.</param>
public sealed record StatValue(
    string Id,
    double Value,
    string Unit,
    DateTimeOffset AsOf,
    long SampleCount);

/// <summary>Descriptive entry in the stat catalogue.</summary>
public sealed record StatDescriptor(string Id, string Name, string Unit, string Description);

/// <summary>
/// One market metric.
/// </summary>
/// <remarks>
/// Metrics are discovered from DI rather than hardcoded into endpoints. Adding a metric means
/// adding one implementation of this interface: the catalogue, the per-stat route, the
/// per-farm route and the WebSocket all pick it up with no further edits (SPEC 8.2).
/// </remarks>
public interface IStat
{
    /// <summary>Stable identifier, kebab-case, used in URLs and subscriptions.</summary>
    string Id { get; }

    /// <summary>Human-readable name.</summary>
    string Name { get; }

    /// <summary>Unit of the computed value.</summary>
    string Unit { get; }

    /// <summary>What the metric means.</summary>
    string Description { get; }

    /// <summary>Compute the current value across all farms.</summary>
    Task<StatValue> ComputeAsync(IMarketMetricsStore store, CancellationToken cancellationToken);

    /// <summary>Compute the current value per farm.</summary>
    Task<IReadOnlyDictionary<FarmOrigin, StatValue>> ComputeByFarmAsync(
        IMarketMetricsStore store,
        CancellationToken cancellationToken);
}
