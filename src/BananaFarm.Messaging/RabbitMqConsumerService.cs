using BananaFarm.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BananaFarm.Messaging;

/// <summary>
/// Base class for a background service that consumes from the shared topic exchange.
/// Subclasses declare which queue to own and which routing keys to bind, then handle
/// deliveries; queue declaration, binding, acknowledgement and error handling live here.
/// </summary>
public abstract class RabbitMqConsumerService : BackgroundService
{
    private readonly RabbitMqConnectionProvider _connections;
    private readonly RabbitMqOptions _options;
    private readonly ILogger _logger;
    private IChannel? _channel;

    protected RabbitMqConsumerService(
        RabbitMqConnectionProvider connections,
        IOptions<RabbitMqOptions> options,
        ILogger logger)
    {
        _connections = connections;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Durable queue this consumer owns.</summary>
    protected abstract string QueueName { get; }

    /// <summary>Routing keys bound from the shared exchange to <see cref="QueueName"/>.</summary>
    protected abstract IReadOnlyList<string> RoutingKeys { get; }

    /// <summary>Handle one delivery. Throwing rejects the message (see remarks on nack).</summary>
    protected abstract Task HandleAsync(
        string routingKey,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connection = await _connections.GetConnectionAsync(stoppingToken).ConfigureAwait(false);

        _channel = await connection.CreateChannelAsync(
                new CreateChannelOptions(
                    publisherConfirmationsEnabled: false,
                    publisherConfirmationTrackingEnabled: false,
                    consumerDispatchConcurrency: _options.ConsumerConcurrency),
                stoppingToken)
            .ConfigureAwait(false);

        await _channel.QueueDeclareAsync(
                queue: QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" },
                cancellationToken: stoppingToken)
            .ConfigureAwait(false);

        foreach (var routingKey in RoutingKeys)
        {
            await _channel.QueueBindAsync(
                    queue: QueueName,
                    exchange: Topics.Exchange,
                    routingKey: routingKey,
                    cancellationToken: stoppingToken)
                .ConfigureAwait(false);
        }

        await _channel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount: _options.PrefetchCount,
                global: false,
                cancellationToken: stoppingToken)
            .ConfigureAwait(false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnReceivedAsync;

        await _channel.BasicConsumeAsync(
                queue: QueueName,
                autoAck: false,
                consumer: consumer,
                cancellationToken: stoppingToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Consuming queue {Queue} bound to [{RoutingKeys}].",
            QueueName,
            string.Join(", ", RoutingKeys));

        // Hold the service open until shutdown; deliveries arrive on the consumer callback.
        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
    }

    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs args)
    {
        if (_channel is null)
        {
            return;
        }

        try
        {
            await HandleAsync(args.RoutingKey, args.Body, CancellationToken.None)
                .ConfigureAwait(false);

            await _channel.BasicAckAsync(args.DeliveryTag, multiple: false)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Rejected without requeue: a message this consumer cannot process will not
            // process on a retry either, and requeueing would spin the consumer. There is no
            // dead-letter queue in this iteration (SPEC 12), so the payload is logged here.
            _logger.LogError(
                ex,
                "Dropping message from {RoutingKey} on queue {Queue}: handler threw.",
                args.RoutingKey,
                QueueName);

            try
            {
                await _channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false)
                    .ConfigureAwait(false);
            }
            catch (Exception nackFailure)
            {
                _logger.LogError(nackFailure, "Could not nack delivery {Tag}.", args.DeliveryTag);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        if (_channel is not null)
        {
            await _channel.DisposeAsync().ConfigureAwait(false);
            _channel = null;
        }
    }
}
