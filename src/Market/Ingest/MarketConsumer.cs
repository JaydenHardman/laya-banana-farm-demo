using BananaFarm.Contracts;
using BananaFarm.Messaging;
using Microsoft.Extensions.Options;

namespace BananaFarm.Market.Ingest;

/// <summary>
/// Consumes every topic the factory publishes and hands each message to
/// <see cref="MarketRecorder"/>.
/// </summary>
/// <remarks>
/// Raw <c>bananaProduction</c> is not bound by default. No market metric needs it, and
/// binding it would route the farm's full 200 messages/second through this service for no
/// gain. Set <c>Market:ConsumeRawProduction</c> to true to include it.
/// </remarks>
public sealed class MarketConsumer : RabbitMqConsumerService
{
    private readonly MarketRecorder _recorder;
    private readonly bool _consumeRawProduction;

    public MarketConsumer(
        MarketRecorder recorder,
        RabbitMqConnectionProvider connections,
        IOptions<RabbitMqOptions> rabbitOptions,
        IOptions<MarketOptions> marketOptions,
        ILogger<MarketConsumer> logger)
        : base(connections, rabbitOptions, logger)
    {
        _recorder = recorder;
        _consumeRawProduction = marketOptions.Value.ConsumeRawProduction;
    }

    protected override string QueueName => "market.ingest";

    protected override IReadOnlyList<string> RoutingKeys => _consumeRawProduction
        ? Topics.All
        : [Topics.FilledBox, Topics.GoldenBanana, Topics.PastRipe, Topics.TooLongToFill];

    protected override Task HandleAsync(
        string routingKey,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken) =>
        _recorder.RecordAsync(routingKey, body, cancellationToken);
}
