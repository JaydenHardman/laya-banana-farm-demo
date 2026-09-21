using BananaFarm.Contracts;
using BananaFarm.Messaging;
using Microsoft.Extensions.Options;

namespace BananaFarm.Factory.Processing;

/// <summary>Consumes raw bananas from the farm.</summary>
public sealed class ProductionConsumer : RabbitMqConsumerService
{
    private readonly BananaProcessor _processor;

    public ProductionConsumer(
        BananaProcessor processor,
        RabbitMqConnectionProvider connections,
        IOptions<RabbitMqOptions> options,
        ILogger<ProductionConsumer> logger)
        : base(connections, options, logger)
        => _processor = processor;

    protected override string QueueName => "factory.production";

    protected override IReadOnlyList<string> RoutingKeys => [Topics.BananaProduction];

    protected override Task HandleAsync(
        string routingKey,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        var banana = BananaJson.Deserialize<Banana>(body.Span);
        return _processor.ProcessAsync(banana, cancellationToken);
    }
}
