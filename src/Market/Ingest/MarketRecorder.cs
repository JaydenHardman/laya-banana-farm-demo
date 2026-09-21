using BananaFarm.Contracts;
using BananaFarm.Market.Metrics;

namespace BananaFarm.Market.Ingest;

/// <summary>
/// Folds one published message into the market's accumulators.
/// </summary>
/// <remarks>
/// Split out from the consumer so the aggregation rules can be tested without a broker.
/// </remarks>
public sealed class MarketRecorder
{
    private readonly IMarketMetricsStore _store;
    private readonly ILogger<MarketRecorder> _logger;

    public MarketRecorder(IMarketMetricsStore store, ILogger<MarketRecorder> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>Record a message published under <paramref name="routingKey"/>.</summary>
    public Task RecordAsync(
        string routingKey,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken) => routingKey switch
    {
        Topics.FilledBox => RecordFilledBoxAsync(
            BananaJson.Deserialize<FilledBoxMessage>(body.Span), cancellationToken),

        Topics.GoldenBanana => RecordBananaAsync(
            MarketMetrics.GoldenBananas, body, cancellationToken),

        Topics.PastRipe => RecordBananaAsync(
            MarketMetrics.PastRipeLoss, body, cancellationToken),

        Topics.TooLongToFill => RecordBananaAsync(
            MarketMetrics.TimedOutBananas, body, cancellationToken),

        // Raw production carries no market signal the factory's output does not already
        // carry; it is only bound when explicitly enabled.
        Topics.BananaProduction => Task.CompletedTask,

        _ => LogUnknown(routingKey),
    };

    /// <summary>
    /// Record a filled box: once globally for the box total, then once per contributing farm
    /// carrying that farm's share.
    /// </summary>
    /// <remarks>
    /// A box holds bananas from several farms. Recording the full total against each of them
    /// would inflate every farm's figures; recording only globally would make the per-farm
    /// slice empty. The share split keeps both readings honest: globally the stat is "mean
    /// value of a box", per farm it is "mean value this farm puts into a box".
    /// </remarks>
    public async Task RecordFilledBoxAsync(
        FilledBoxMessage box,
        CancellationToken cancellationToken)
    {
        await _store
            .RecordGlobalAsync(MarketMetrics.BoxPrice, (double)box.TotalPrice, cancellationToken)
            .ConfigureAwait(false);

        var shares = box.Bananas
            .GroupBy(banana => banana.FarmOrigin)
            .Select(group => (Origin: group.Key, Share: group.Sum(banana => banana.Price)));

        foreach (var (origin, share) in shares)
        {
            await _store
                .RecordFarmAsync(MarketMetrics.BoxPrice, origin, (double)share, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task RecordBananaAsync(
        string metric,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        var banana = BananaJson.Deserialize<TrackedBanana>(body.Span);

        await _store
            .RecordAsync(metric, banana.FarmOrigin, (double)banana.Price, cancellationToken)
            .ConfigureAwait(false);
    }

    private Task LogUnknown(string routingKey)
    {
        _logger.LogWarning(
            "Ignoring message with unrecognised routing key {RoutingKey}.",
            routingKey);

        return Task.CompletedTask;
    }
}
