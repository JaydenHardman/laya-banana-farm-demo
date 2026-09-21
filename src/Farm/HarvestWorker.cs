using System.Diagnostics;
using BananaFarm.Contracts;
using BananaFarm.Farm.Generation;
using BananaFarm.Farm.Waves;
using BananaFarm.Messaging;
using Microsoft.Extensions.Options;

namespace BananaFarm.Farm;

/// <summary>
/// The farm's hot loop: wake on a fixed tick, ask the wave controller for the current rate,
/// draw a Poisson count for the elapsed interval, generate that many bananas and publish
/// them as one batch.
/// </summary>
public sealed class HarvestWorker : BackgroundService
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(10);

    private readonly BananaGenerator _generator;
    private readonly WaveRateController _waves;
    private readonly IMessagePublisher _publisher;
    private readonly IRandomSource _random;
    private readonly FarmOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<HarvestWorker> _logger;

    public HarvestWorker(
        BananaGenerator generator,
        WaveRateController waves,
        IMessagePublisher publisher,
        IRandomSource random,
        IOptions<FarmOptions> options,
        TimeProvider time,
        ILogger<HarvestWorker> logger)
    {
        _generator = generator;
        _waves = waves;
        _publisher = publisher;
        _random = random;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tick = TimeSpan.FromMilliseconds(_options.TickMilliseconds);

        _logger.LogInformation(
            "Harvesting at a base rate of {Rate}/s, emitting every {Tick}ms.",
            _options.BaseRatePerSecond,
            _options.TickMilliseconds);

        using var timer = new PeriodicTimer(tick, _time);

        var producedSinceReport = 0L;
        var reportStopwatch = Stopwatch.StartNew();

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var rate = _waves.Advance(tick, _options.BaseRatePerSecond);
                var count = PoissonSampler.Sample(rate * tick.TotalSeconds, _random);

                if (count > 0)
                {
                    var bananas = _generator.Next(count);
                    await _publisher
                        .PublishBatchAsync(Topics.BananaProduction, bananas, stoppingToken)
                        .ConfigureAwait(false);

                    producedSinceReport += count;
                }

                if (reportStopwatch.Elapsed >= ReportInterval)
                {
                    _logger.LogInformation(
                        "Produced {Count} bananas in {Seconds:F1}s ({Rate:F1}/s), regime {Regime} at {Multiplier:F2}x.",
                        producedSinceReport,
                        reportStopwatch.Elapsed.TotalSeconds,
                        producedSinceReport / reportStopwatch.Elapsed.TotalSeconds,
                        _waves.Regime,
                        _waves.Multiplier);

                    producedSinceReport = 0;
                    reportStopwatch.Restart();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // One bad tick must not kill the farm; the next tick retries.
                _logger.LogError(ex, "Harvest tick failed; continuing.");
            }
        }
    }
}
