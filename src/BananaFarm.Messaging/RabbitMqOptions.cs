using System.ComponentModel.DataAnnotations;

namespace BananaFarm.Messaging;

/// <summary>Connection and topology settings for the shared RabbitMQ topic exchange.</summary>
public sealed class RabbitMqOptions
{
    /// <summary>Configuration section these options bind from.</summary>
    public const string SectionName = "RabbitMq";

    /// <summary>AMQP connection string, e.g. <c>amqp://guest:guest@rabbitmq:5672/</c>.</summary>
    [Required]
    public string Uri { get; set; } = "amqp://guest:guest@rabbitmq:5672/";

    /// <summary>How many messages a consumer may hold unacknowledged at once.</summary>
    [Range(1, 10_000)]
    public ushort PrefetchCount { get; set; } = 200;

    /// <summary>How many handler invocations may run concurrently per consumer.</summary>
    [Range(1, 256)]
    public ushort ConsumerConcurrency { get; set; } = 8;

    /// <summary>Seconds to keep retrying the initial connection before giving up.</summary>
    [Range(1, 600)]
    public int ConnectTimeoutSeconds { get; set; } = 120;
}
