using BananaFarm.Contracts;
using BananaFarm.Messaging;
using Microsoft.Extensions.Options;

namespace BananaFarm.Factory.Boxing;

/// <summary>
/// Flushes boxes that never filled.
/// </summary>
/// <remarks>
/// A box whose first banana arrived more than <see cref="FactoryOptions.BoxTimeoutSeconds"/>
/// ago has its contents published individually to <c>tooLongToFill</c> and is discarded.
/// Without this, a rare grade/ripeness combination would hold bananas forever.
/// </remarks>
public sealed class BoxTimeoutSweeper : BackgroundService
{
    private readonly IBoxRepository _boxes;
    private readonly IMessagePublisher _publisher;
    private readonly FactoryOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<BoxTimeoutSweeper> _logger;

    public BoxTimeoutSweeper(
        IBoxRepository boxes,
        IMessagePublisher publisher,
        IOptions<FactoryOptions> options,
        TimeProvider time,
        ILogger<BoxTimeoutSweeper> logger)
    {
        _boxes = boxes;
        _publisher = publisher;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMilliseconds(_options.SweepIntervalMilliseconds);
        var timeout = TimeSpan.FromSeconds(_options.BoxTimeoutSeconds);

        using var timer = new PeriodicTimer(interval, _time);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await SweepAsync(timeout, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Box sweep failed; will retry on the next tick.");
            }
        }
    }

    private async Task SweepAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var cutoff = _time.GetUtcNow() - timeout;
        var expired = await _boxes.RemoveExpiredAsync(cutoff, cancellationToken)
            .ConfigureAwait(false);

        if (expired.Count == 0)
        {
            return;
        }

        var bananas = expired.SelectMany(box => box.Bananas)
            .Select(banana => banana with { Reason = PublishReason.BoxTimedOut })
            .ToList();

        await _publisher
            .PublishBatchAsync(Topics.TooLongToFill, bananas, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Flushed {Boxes} box(es) that did not fill within {Timeout}s, releasing {Bananas} bananas.",
            expired.Count,
            timeout.TotalSeconds,
            bananas.Count);
    }
}
