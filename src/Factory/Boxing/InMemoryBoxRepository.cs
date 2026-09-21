using BananaFarm.Contracts;
using Microsoft.Extensions.Options;

namespace BananaFarm.Factory.Boxing;

/// <summary>
/// In-process <see cref="IBoxRepository"/>.
/// </summary>
/// <remarks>
/// Correct and fast for a single factory instance, and the implementation the box semantics
/// are unit-tested against. It cannot coordinate across replicas — scale the factory out and
/// each instance would fill its own boxes — so <see cref="RedisBoxRepository"/> is the
/// default. Selected with <c>Factory:BoxStore=memory</c>.
/// </remarks>
public sealed class InMemoryBoxRepository : IBoxRepository
{
    private readonly int _capacity;
    private readonly Lock _gate = new();
    private readonly Dictionary<BoxKey, OpenBox> _boxes = [];

    public InMemoryBoxRepository(IOptions<FactoryOptions> options) =>
        _capacity = options.Value.BoxCapacity;

    public Task<FilledBoxMessage?> AddAsync(
        BoxKey key,
        TrackedBanana banana,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_boxes.TryGetValue(key, out var box))
            {
                box = new OpenBox(Guid.NewGuid(), now);
                _boxes[key] = box;
            }

            box.Bananas.Add(banana);

            if (box.Bananas.Count < _capacity)
            {
                return Task.FromResult<FilledBoxMessage?>(null);
            }

            _boxes.Remove(key);

            var filled = new FilledBoxMessage(
                BoxId: box.BoxId,
                Grade: key.Grade,
                RipenessLevel: key.RipenessLevel,
                Bananas: [.. box.Bananas],
                TotalPrice: box.Bananas.Sum(item => item.Price),
                OpenedAt: box.OpenedAt,
                FilledAt: now);

            return Task.FromResult<FilledBoxMessage?>(filled);
        }
    }

    public Task<IReadOnlyList<ExpiredBox>> RemoveExpiredAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var expiredKeys = _boxes
                .Where(pair => pair.Value.OpenedAt <= cutoff)
                .Select(pair => pair.Key)
                .ToList();

            var expired = new List<ExpiredBox>(expiredKeys.Count);

            foreach (var key in expiredKeys)
            {
                var box = _boxes[key];
                _boxes.Remove(key);
                expired.Add(new ExpiredBox(box.BoxId, key, box.OpenedAt, [.. box.Bananas]));
            }

            return Task.FromResult<IReadOnlyList<ExpiredBox>>(expired);
        }
    }

    public Task<int> CountOpenAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_boxes.Count);
        }
    }

    private sealed record OpenBox(Guid BoxId, DateTimeOffset OpenedAt)
    {
        public List<TrackedBanana> Bananas { get; } = [];
    }
}
