using System.Net.WebSockets;
using System.Text;
using BananaFarm.Contracts;
using BananaFarm.Market.Metrics;
using BananaFarm.Market.Stats;
using Microsoft.Extensions.Options;

namespace BananaFarm.Market.Live;

/// <summary>
/// Serves <c>/stats/live</c>: a client subscribes to stat ids and receives coalesced
/// snapshots on an interval.
/// </summary>
/// <remarks>
/// Stats move hundreds of times a second, so pushing on every change would flood any
/// consumer. Snapshots are sent on a fixed interval and skipped entirely when nothing the
/// subscriber asked for has moved, which keeps an idle dashboard silent.
/// </remarks>
public sealed class StatsLiveHandler
{
    private const int MaxSubscribeFrameBytes = 8 * 1024;

    private readonly StatRegistry _registry;
    private readonly IMarketMetricsStore _store;
    private readonly MarketOptions _options;
    private readonly ILogger<StatsLiveHandler> _logger;

    public StatsLiveHandler(
        StatRegistry registry,
        IMarketMetricsStore store,
        IOptions<MarketOptions> options,
        ILogger<StatsLiveHandler> logger)
    {
        _registry = registry;
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Run one subscriber's connection until it disconnects or the host stops.</summary>
    public async Task HandleAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var request = await ReceiveSubscribeFrameAsync(socket, cancellationToken)
            .ConfigureAwait(false);

        if (request is null)
        {
            return;
        }

        if (!TryResolve(request.Subscribe, out var stats, out var unknown))
        {
            await SendAsync(
                    socket,
                    new ErrorFrame(
                        $"Unknown stat id(s): {string.Join(", ", unknown)}.",
                        _registry.Ids),
                    cancellationToken)
                .ConfigureAwait(false);

            await socket.CloseAsync(
                    WebSocketCloseStatus.InvalidPayloadData,
                    "Unknown stat id",
                    cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Clamp(
            request.IntervalSeconds ?? _options.DefaultLiveIntervalSeconds,
            _options.MinLiveIntervalSeconds,
            _options.MaxLiveIntervalSeconds));

        await SendAsync(
                socket,
                new SubscribedFrame([.. stats.Select(stat => stat.Id)], (int)interval.TotalSeconds),
                cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "WebSocket subscriber attached to [{Stats}] every {Interval}s.",
            string.Join(", ", stats.Select(stat => stat.Id)),
            interval.TotalSeconds);

        await PushLoopAsync(socket, stats, interval, cancellationToken).ConfigureAwait(false);
    }

    private async Task PushLoopAsync(
        WebSocket socket,
        IReadOnlyList<IStat> stats,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        string? lastSignature = null;
        using var timer = new PeriodicTimer(interval);

        // Send one snapshot immediately so a new subscriber is not blank until the first tick.
        do
        {
            if (socket.State != WebSocketState.Open)
            {
                return;
            }

            var values = new Dictionary<string, StatValue>(stats.Count);

            foreach (var stat in stats)
            {
                values[stat.Id] = await stat.ComputeAsync(_store, cancellationToken)
                    .ConfigureAwait(false);
            }

            var signature = BuildSignature(values);

            if (signature != lastSignature)
            {
                lastSignature = signature;

                await SendAsync(
                        socket,
                        new SnapshotFrame(DateTimeOffset.UtcNow, values),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Value-and-sample-count fingerprint used to skip a tick where nothing moved. Excludes
    /// the timestamp, which always changes.
    /// </summary>
    private static string BuildSignature(Dictionary<string, StatValue> values)
    {
        var builder = new StringBuilder();

        foreach (var (id, value) in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Append(id).Append('=').Append(value.Value)
                .Append('/').Append(value.SampleCount).Append(';');
        }

        return builder.ToString();
    }

    private bool TryResolve(
        IReadOnlyList<string>? requested,
        out IReadOnlyList<IStat> stats,
        out IReadOnlyList<string> unknown)
    {
        if (requested is null || requested.Count == 0)
        {
            stats = _registry.All;
            unknown = [];
            return true;
        }

        var resolved = new List<IStat>(requested.Count);
        var missing = new List<string>();

        foreach (var id in requested)
        {
            if (_registry.TryGet(id, out var stat))
            {
                resolved.Add(stat);
            }
            else
            {
                missing.Add(id);
            }
        }

        stats = resolved;
        unknown = missing;
        return missing.Count == 0;
    }

    private async Task<SubscribeFrame?> ReceiveSubscribeFrameAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxSubscribeFrameBytes];
        var received = 0;

        while (true)
        {
            var result = await socket
                .ReceiveAsync(new ArraySegment<byte>(buffer, received, buffer.Length - received),
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            received += result.Count;

            if (result.EndOfMessage)
            {
                break;
            }

            if (received >= buffer.Length)
            {
                await SendAsync(
                        socket,
                        new ErrorFrame(
                            $"Subscription frame exceeded {MaxSubscribeFrameBytes} bytes.",
                            _registry.Ids),
                        cancellationToken)
                    .ConfigureAwait(false);

                return null;
            }
        }

        try
        {
            return BananaJson.Deserialize<SubscribeFrame>(buffer.AsSpan(0, received));
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Rejected a malformed subscription frame.");

            await SendAsync(
                    socket,
                    new ErrorFrame(
                        "Could not parse the subscription frame. Expected " +
                        "{\"subscribe\": [\"stat-id\"], \"intervalSeconds\": 5}.",
                        _registry.Ids),
                    cancellationToken)
                .ConfigureAwait(false);

            return null;
        }
    }

    private static Task SendAsync<T>(WebSocket socket, T frame, CancellationToken cancellationToken) =>
        socket.SendAsync(
            BananaJson.SerializeToUtf8Bytes(frame),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
}
