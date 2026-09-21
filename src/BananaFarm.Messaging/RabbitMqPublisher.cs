using BananaFarm.Contracts;
using RabbitMQ.Client;

namespace BananaFarm.Messaging;

/// <summary>
/// <see cref="IMessagePublisher"/> backed by a RabbitMQ topic exchange.
/// </summary>
/// <remarks>
/// Publisher confirmations are disabled deliberately. This system generates disposable
/// synthetic data at 200 messages/second; waiting for a broker ack per message would cap
/// throughput far below target, and a lost banana costs nothing. A system carrying real
/// value should enable them in <see cref="CreateChannelOptions"/>.
/// </remarks>
public sealed class RabbitMqPublisher : IMessagePublisher, IAsyncDisposable
{
    private readonly RabbitMqConnectionProvider _connections;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    public RabbitMqPublisher(RabbitMqConnectionProvider connections) => _connections = connections;

    public async Task PublishAsync<T>(
        string routingKey,
        T message,
        CancellationToken cancellationToken)
    {
        var channel = await GetChannelAsync(cancellationToken).ConfigureAwait(false);
        await PublishCoreAsync(channel, routingKey, message, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishBatchAsync<T>(
        string routingKey,
        IReadOnlyCollection<T> messages,
        CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return;
        }

        var channel = await GetChannelAsync(cancellationToken).ConfigureAwait(false);
        foreach (var message in messages)
        {
            await PublishCoreAsync(channel, routingKey, message, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task PublishCoreAsync<T>(
        IChannel channel,
        string routingKey,
        T message,
        CancellationToken cancellationToken)
    {
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
        };

        await channel.BasicPublishAsync(
                exchange: Topics.Exchange,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: properties,
                body: BananaJson.SerializeToUtf8Bytes(message),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_channel is { IsOpen: true })
            {
                return _channel;
            }

            var connection = await _connections.GetConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

            _channel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(
                        publisherConfirmationsEnabled: false,
                        publisherConfirmationTrackingEnabled: false),
                    cancellationToken)
                .ConfigureAwait(false);

            return _channel;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync().ConfigureAwait(false);
        }

        _gate.Dispose();
    }
}
