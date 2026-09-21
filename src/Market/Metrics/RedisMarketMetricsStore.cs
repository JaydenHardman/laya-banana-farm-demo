using System.Globalization;
using BananaFarm.Contracts;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BananaFarm.Market.Metrics;

/// <summary>Redis-backed <see cref="IMarketMetricsStore"/>.</summary>
/// <remarks>
/// Each metric is one hash holding <c>sum</c> and <c>count</c>, incremented in place. Writes
/// are O(1) regardless of how many bananas have been processed, and both increments go in a
/// single pipelined batch.
/// </remarks>
public sealed class RedisMarketMetricsStore : IMarketMetricsStore
{
    private const string SumField = "sum";
    private const string CountField = "count";

    private static readonly FarmOrigin[] AllOrigins = Enum.GetValues<FarmOrigin>();

    private readonly IConnectionMultiplexer _redis;
    private readonly string _prefix;

    public RedisMarketMetricsStore(IConnectionMultiplexer redis, IOptions<MarketOptions> options)
    {
        _redis = redis;
        _prefix = $"{options.Value.KeyPrefix}:market:";
    }

    public Task RecordGlobalAsync(
        string metric,
        double value,
        CancellationToken cancellationToken) =>
        IncrementAsync(GlobalKey(metric), value);

    public Task RecordFarmAsync(
        string metric,
        FarmOrigin origin,
        double value,
        CancellationToken cancellationToken) =>
        IncrementAsync(FarmKey(metric, origin), value);

    private async Task IncrementAsync(string key, double value)
    {
        var batch = _redis.GetDatabase().CreateBatch();

        var sum = batch.HashIncrementAsync(key, SumField, value);
        var count = batch.HashIncrementAsync(key, CountField, 1);

        batch.Execute();
        await Task.WhenAll(sum, count).ConfigureAwait(false);
    }

    public async Task<MetricAggregate> GetAsync(string metric, CancellationToken cancellationToken)
    {
        var entries = await _redis.GetDatabase()
            .HashGetAllAsync(GlobalKey(metric))
            .ConfigureAwait(false);

        return ToAggregate(entries);
    }

    public async Task<IReadOnlyDictionary<FarmOrigin, MetricAggregate>> GetByFarmAsync(
        string metric,
        CancellationToken cancellationToken)
    {
        var database = _redis.GetDatabase();

        var reads = AllOrigins.ToDictionary(
            origin => origin,
            origin => database.HashGetAllAsync(FarmKey(metric, origin)));

        await Task.WhenAll(reads.Values).ConfigureAwait(false);

        return reads.ToDictionary(pair => pair.Key, pair => ToAggregate(pair.Value.Result));
    }

    private static MetricAggregate ToAggregate(HashEntry[] entries)
    {
        if (entries.Length == 0)
        {
            return MetricAggregate.Empty;
        }

        var sum = 0d;
        var count = 0L;

        foreach (var entry in entries)
        {
            var name = entry.Name.ToString();

            if (name == SumField)
            {
                double.TryParse(
                    entry.Value.ToString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out sum);
            }
            else if (name == CountField)
            {
                long.TryParse(
                    entry.Value.ToString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out count);
            }
        }

        return new MetricAggregate(sum, count);
    }

    private string GlobalKey(string metric) => $"{_prefix}{metric}";

    private string FarmKey(string metric, FarmOrigin origin) => $"{_prefix}{metric}:farm:{origin}";
}
