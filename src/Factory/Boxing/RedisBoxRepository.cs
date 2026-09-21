using System.Globalization;
using BananaFarm.Contracts;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BananaFarm.Factory.Boxing;

/// <summary>
/// Redis-backed <see cref="IBoxRepository"/>. The default store.
/// </summary>
/// <remarks>
/// <para>
/// Both operations run as Lua scripts so they execute atomically on the server. "Append and
/// report if full" cannot be a read-modify-write from the client: several consumer threads
/// (and, later, several factory replicas) add to the same box at once, and interleaving
/// would produce boxes of nineteen or twenty-one bananas. A distributed lock would also
/// work, and would be slower and easier to get wrong.
/// </para>
/// <para>Layout per open box:</para>
/// <list type="bullet">
/// <item><c>{prefix}:box:{key}</c> — list of serialised bananas.</item>
/// <item><c>{prefix}:box:{key}:meta</c> — hash of boxId, openedAt, grade, ripenessLevel.</item>
/// <item><c>{prefix}:box:index</c> — sorted set of box keys scored by open time, so the
/// sweeper finds expired boxes without scanning the keyspace.</item>
/// </list>
/// <para>
/// Single-node Redis only. The sweep script derives each box's meta key inside Lua rather
/// than declaring it in KEYS, which Redis Cluster rejects because it cannot verify the slot.
/// Moving to a cluster means sweeping in two round-trips: read the expired ids, then delete
/// each box with its keys declared.
/// </para>
/// </remarks>
public sealed class RedisBoxRepository : IBoxRepository
{
    private const string AddScript = """
        local length = redis.call('RPUSH', KEYS[1], ARGV[1])

        if length == 1 then
            redis.call('HSET', KEYS[2],
                'boxId', ARGV[3],
                'openedAt', ARGV[4],
                'grade', ARGV[5],
                'ripenessLevel', ARGV[6])
            redis.call('ZADD', KEYS[3], ARGV[7], KEYS[1])
        end

        if length < tonumber(ARGV[2]) then
            return nil
        end

        local items = redis.call('LRANGE', KEYS[1], 0, -1)
        local meta = redis.call('HMGET', KEYS[2], 'boxId', 'openedAt')
        redis.call('DEL', KEYS[1], KEYS[2])
        redis.call('ZREM', KEYS[3], KEYS[1])

        local reply = { meta[1], meta[2] }
        for i = 1, #items do
            reply[#reply + 1] = items[i]
        end
        return reply
        """;

    private const string SweepScript = """
        local expired = redis.call('ZRANGEBYSCORE', KEYS[1], '-inf', ARGV[1], 'LIMIT', 0, ARGV[2])
        local out = {}

        for _, listKey in ipairs(expired) do
            local metaKey = listKey .. ':meta'
            local items = redis.call('LRANGE', listKey, 0, -1)
            local meta = redis.call('HMGET', metaKey, 'boxId', 'openedAt', 'grade', 'ripenessLevel')

            redis.call('DEL', listKey, metaKey)
            redis.call('ZREM', KEYS[1], listKey)

            local box = { meta[1] or '', meta[2] or '', meta[3] or '', meta[4] or '' }
            for i = 1, #items do
                box[#box + 1] = items[i]
            end
            out[#out + 1] = box
        end

        return out
        """;

    /// <summary>Upper bound on boxes flushed in one sweep, so a backlog cannot block Redis.</summary>
    private const int MaxBoxesPerSweep = 500;

    private readonly IConnectionMultiplexer _redis;
    private readonly int _capacity;
    private readonly string _boxPrefix;
    private readonly string _indexKey;

    public RedisBoxRepository(
        IConnectionMultiplexer redis,
        IOptions<RedisOptions> redisOptions,
        IOptions<FactoryOptions> factoryOptions)
    {
        _redis = redis;
        _capacity = factoryOptions.Value.BoxCapacity;
        _boxPrefix = $"{redisOptions.Value.KeyPrefix}:box:";
        _indexKey = $"{redisOptions.Value.KeyPrefix}:box:index";
    }

    public async Task<FilledBoxMessage?> AddAsync(
        BoxKey key,
        TrackedBanana banana,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var listKey = _boxPrefix + key;

        var result = await _redis.GetDatabase().ScriptEvaluateAsync(
                AddScript,
                keys: [listKey, $"{listKey}:meta", _indexKey],
                values:
                [
                    BananaJson.SerializeToUtf8Bytes(banana),
                    _capacity,
                    Guid.NewGuid().ToString(),
                    now.ToUnixTimeMilliseconds(),
                    key.Grade.ToString(),
                    key.RipenessLevel,
                    now.ToUnixTimeMilliseconds(),
                ])
            .ConfigureAwait(false);

        if (result.IsNull)
        {
            return null;
        }

        var reply = (RedisResult[])result!;
        var boxId = ParseGuid(reply[0]);
        var openedAt = ParseTimestamp(reply[1]);
        var bananas = ParseBananas(reply, startIndex: 2);

        return new FilledBoxMessage(
            BoxId: boxId,
            Grade: key.Grade,
            RipenessLevel: key.RipenessLevel,
            Bananas: bananas,
            TotalPrice: bananas.Sum(item => item.Price),
            OpenedAt: openedAt,
            FilledAt: now);
    }

    public async Task<IReadOnlyList<ExpiredBox>> RemoveExpiredAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        var result = await _redis.GetDatabase().ScriptEvaluateAsync(
                SweepScript,
                keys: [_indexKey],
                values: [cutoff.ToUnixTimeMilliseconds(), MaxBoxesPerSweep])
            .ConfigureAwait(false);

        if (result.IsNull)
        {
            return [];
        }

        var boxes = (RedisResult[])result!;
        var expired = new List<ExpiredBox>(boxes.Length);

        foreach (var entry in boxes)
        {
            var fields = (RedisResult[])entry!;

            if (fields.Length < 4)
            {
                continue;
            }

            var bananas = ParseBananas(fields, startIndex: 4);

            if (bananas.Count == 0)
            {
                continue;
            }

            expired.Add(new ExpiredBox(
                BoxId: ParseGuid(fields[0]),
                Key: new BoxKey(ParseGrade(fields[2]), ParseLevel(fields[3])),
                OpenedAt: ParseTimestamp(fields[1]),
                Bananas: bananas));
        }

        return expired;
    }

    public async Task<int> CountOpenAsync(CancellationToken cancellationToken) =>
        (int)await _redis.GetDatabase().SortedSetLengthAsync(_indexKey).ConfigureAwait(false);

    private static List<TrackedBanana> ParseBananas(RedisResult[] reply, int startIndex)
    {
        var bananas = new List<TrackedBanana>(Math.Max(0, reply.Length - startIndex));

        for (var i = startIndex; i < reply.Length; i++)
        {
            bananas.Add(BananaJson.Deserialize<TrackedBanana>((byte[])reply[i]!));
        }

        return bananas;
    }

    private static Guid ParseGuid(RedisResult value) =>
        Guid.TryParse((string?)value, out var parsed) ? parsed : Guid.Empty;

    private static DateTimeOffset ParseTimestamp(RedisResult value) =>
        long.TryParse((string?)value, CultureInfo.InvariantCulture, out var epochMilliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(epochMilliseconds)
            : DateTimeOffset.UnixEpoch;

    private static ClassifiedGrade ParseGrade(RedisResult value) =>
        Enum.TryParse<ClassifiedGrade>((string?)value, out var parsed)
            ? parsed
            : ClassifiedGrade.Other;

    private static int ParseLevel(RedisResult value) =>
        int.TryParse((string?)value, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
}
