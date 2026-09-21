namespace BananaFarm.Messaging;

/// <summary>
/// Publishes messages to the shared topic exchange. The abstraction exists so the broker can
/// be swapped (for RabbitMQ Streams, or anything else) without touching service logic.
/// </summary>
public interface IMessagePublisher
{
    /// <summary>Publish a single message under <paramref name="routingKey"/>.</summary>
    Task PublishAsync<T>(string routingKey, T message, CancellationToken cancellationToken);

    /// <summary>
    /// Publish many messages under one routing key in a single broker round-trip. Used on the
    /// farm's hot path, where per-message round-trips would cap throughput.
    /// </summary>
    Task PublishBatchAsync<T>(
        string routingKey,
        IReadOnlyCollection<T> messages,
        CancellationToken cancellationToken);
}
