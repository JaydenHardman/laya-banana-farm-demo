using System.Text.Json;
using BananaFarm.Contracts;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BananaFarm.Factory.Classification;

/// <summary>Redis-backed <see cref="IClassificationCache"/>.</summary>
public sealed class RedisClassificationCache : IClassificationCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisClassificationCache> _logger;
    private readonly string _keyPrefix;
    private readonly TimeSpan _ttl;

    public RedisClassificationCache(
        IConnectionMultiplexer redis,
        IOptions<RedisOptions> redisOptions,
        IOptions<FactoryOptions> factoryOptions,
        ILogger<RedisClassificationCache> logger)
    {
        _redis = redis;
        _logger = logger;
        _keyPrefix = $"{redisOptions.Value.KeyPrefix}:classification:";
        _ttl = TimeSpan.FromSeconds(factoryOptions.Value.CacheTtlSeconds);
    }

    public async Task<BananaClassification?> GetAsync(
        string bucketKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await _redis.GetDatabase()
                .StringGetAsync(_keyPrefix + bucketKey)
                .ConfigureAwait(false);

            if (!value.HasValue)
            {
                return null;
            }

            return JsonSerializer.Deserialize<BananaClassification>(
                value.ToString(),
                BananaJson.Options);
        }
        catch (Exception ex) when (ex is RedisException or JsonException)
        {
            // A cache is an optimisation. If it is unavailable or holds a stale shape, fall
            // through to the model rather than failing the banana.
            _logger.LogWarning(ex, "Classification cache read failed for {Bucket}.", bucketKey);
            return null;
        }
    }

    public async Task SetAsync(
        string bucketKey,
        BananaClassification classification,
        CancellationToken cancellationToken)
    {
        try
        {
            await _redis.GetDatabase()
                .StringSetAsync(
                    _keyPrefix + bucketKey,
                    JsonSerializer.Serialize(classification, BananaJson.Options),
                    _ttl)
                .ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Classification cache write failed for {Bucket}.", bucketKey);
        }
    }
}
