using BananaFarm.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace BananaFarm.Messaging;

/// <summary>
/// Owns the single AMQP connection for a service and declares the shared topic exchange.
/// </summary>
/// <remarks>
/// Compose starts every service at once, so the broker is routinely unreachable for the
/// first few seconds. Connection establishment retries rather than crash-looping the
/// container.
/// </remarks>
public sealed class RabbitMqConnectionProvider : IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnectionProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public RabbitMqConnectionProvider(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqConnectionProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Returns the shared connection, opening it on first use.</summary>
    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            _connection = await ConnectWithRetryAsync(cancellationToken).ConfigureAwait(false);
            await DeclareTopologyAsync(_connection, cancellationToken).ConfigureAwait(false);
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IConnection> ConnectWithRetryAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri(_options.Uri),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            ConsumerDispatchConcurrency = _options.ConsumerConcurrency,
        };

        var deadline = DateTimeOffset.UtcNow.AddSeconds(_options.ConnectTimeoutSeconds);
        var delay = TimeSpan.FromSeconds(1);

        while (true)
        {
            try
            {
                return await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    _logger.LogError(
                        ex,
                        "Could not reach RabbitMQ within {Timeout}s. Check that the broker is running and RabbitMq:Uri is correct.",
                        _options.ConnectTimeoutSeconds);
                    throw;
                }

                _logger.LogWarning(
                    "RabbitMQ not reachable yet ({Reason}); retrying in {Delay}s.",
                    ex.Message,
                    delay.TotalSeconds);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 10));
            }
        }
    }

    private static async Task DeclareTopologyAsync(
        IConnection connection,
        CancellationToken cancellationToken)
    {
        await using var channel = await connection
            .CreateChannelAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await channel.ExchangeDeclareAsync(
                exchange: Topics.Exchange,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        _gate.Dispose();
    }
}
