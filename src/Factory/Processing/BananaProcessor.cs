using BananaFarm.Contracts;
using BananaFarm.Factory.Boxing;
using BananaFarm.Factory.Classification;
using BananaFarm.Factory.Routing;
using BananaFarm.Messaging;

namespace BananaFarm.Factory.Processing;

/// <summary>
/// Classifies one banana and sends it wherever the rules say it goes.
/// </summary>
public sealed class BananaProcessor
{
    private readonly IBananaClassifier _classifier;
    private readonly IBoxRepository _boxes;
    private readonly IMessagePublisher _publisher;
    private readonly TimeProvider _time;
    private readonly ILogger<BananaProcessor> _logger;

    public BananaProcessor(
        IBananaClassifier classifier,
        IBoxRepository boxes,
        IMessagePublisher publisher,
        TimeProvider time,
        ILogger<BananaProcessor> logger)
    {
        _classifier = classifier;
        _boxes = boxes;
        _publisher = publisher;
        _time = time;
        _logger = logger;
    }

    /// <summary>Classify, route, and publish or box a single banana.</summary>
    public async Task ProcessAsync(Banana banana, CancellationToken cancellationToken)
    {
        var classification = await _classifier.ClassifyAsync(banana, cancellationToken)
            .ConfigureAwait(false);

        var destination = BananaRouter.Decide(classification);
        var now = _time.GetUtcNow();

        var tracked = new TrackedBanana(
            BananaId: banana.Id,
            FarmOrigin: banana.FarmOrigin,
            Price: banana.Price,
            Reason: BananaRouter.ReasonFor(destination),
            Classification: classification,
            Banana: banana,
            PublishedAt: now);

        if (destination is BananaDestination.PastRipe or BananaDestination.GoldenBanana)
        {
            await _publisher
                .PublishAsync(BananaRouter.TopicFor(destination), tracked, cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        var key = BoxKey.From(classification);
        var filled = await _boxes.AddAsync(key, tracked, now, cancellationToken)
            .ConfigureAwait(false);

        if (filled is null)
        {
            return;
        }

        await _publisher.PublishAsync(Topics.FilledBox, filled, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogDebug(
            "Filled box {BoxId} ({Key}) with {Count} bananas worth {Total:F2}.",
            filled.BoxId,
            key,
            filled.Count,
            filled.TotalPrice);
    }
}
