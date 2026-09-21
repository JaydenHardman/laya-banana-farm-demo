using System.Collections.Concurrent;
using BananaFarm.Contracts;
using BananaFarm.Factory.Classification;
using BananaFarm.Messaging;

namespace BananaFarm.Factory.Tests;

/// <summary>Captures everything published, so tests can assert on routing.</summary>
internal sealed class FakePublisher : IMessagePublisher
{
    public ConcurrentBag<(string RoutingKey, object Message)> Published { get; } = [];

    public Task PublishAsync<T>(string routingKey, T message, CancellationToken cancellationToken)
    {
        Published.Add((routingKey, message!));
        return Task.CompletedTask;
    }

    public Task PublishBatchAsync<T>(
        string routingKey,
        IReadOnlyCollection<T> messages,
        CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            Published.Add((routingKey, message!));
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<T> MessagesOn<T>(string routingKey) =>
        [.. Published.Where(item => item.RoutingKey == routingKey).Select(item => item.Message).OfType<T>()];
}

/// <summary>In-memory <see cref="IClassificationCache"/>.</summary>
internal sealed class FakeCache : IClassificationCache
{
    private readonly ConcurrentDictionary<string, BananaClassification> _entries = new();

    public int Reads { get; private set; }

    public int Writes { get; private set; }

    public Task<BananaClassification?> GetAsync(string bucketKey, CancellationToken cancellationToken)
    {
        Reads++;
        return Task.FromResult(_entries.GetValueOrDefault(bucketKey));
    }

    public Task SetAsync(
        string bucketKey,
        BananaClassification classification,
        CancellationToken cancellationToken)
    {
        Writes++;
        _entries[bucketKey] = classification;
        return Task.CompletedTask;
    }
}

/// <summary>Records every batch it is asked to classify and answers from a fixed verdict.</summary>
internal sealed class FakeClassificationClient : IClassificationClient
{
    private readonly BananaClassification _verdict;

    public FakeClassificationClient(BananaClassification? verdict = null) =>
        _verdict = verdict ?? TestData.Ordinary;

    public ConcurrentBag<int> BatchSizes { get; } = [];

    public int BananasClassified => BatchSizes.Sum();

    public Task<IReadOnlyList<BananaClassification>> ClassifyAsync(
        IReadOnlyList<Banana> bananas,
        CancellationToken cancellationToken)
    {
        BatchSizes.Add(bananas.Count);

        IReadOnlyList<BananaClassification> results =
            [.. Enumerable.Repeat(_verdict, bananas.Count)];

        return Task.FromResult(results);
    }
}

/// <summary>Shared fixtures.</summary>
internal static class TestData
{
    public static readonly BananaClassification Ordinary = new(ClassifiedGrade.B, 0.9, 2, 0.01);

    public static readonly BananaClassification Golden = new(ClassifiedGrade.Golden, 0.99, 2, 0.01);

    public static readonly BananaClassification Rotten = new(ClassifiedGrade.C, 0.8, 3, 0.97);

    public static readonly BananaClassification GoldenAndRotten =
        new(ClassifiedGrade.Golden, 0.95, 3, 0.99);

    public static Banana CreateBanana(
        decimal price = 7.50m,
        FarmOrigin origin = FarmOrigin.XFarm,
        float ripeness = 0.5f,
        float weight = 120f,
        BananaGrade grade = BananaGrade.B) =>
        new(
            Id: Guid.NewGuid(),
            DateHarvested: DateTimeOffset.UnixEpoch,
            FarmOrigin: origin,
            Weight: weight,
            Ripeness: ripeness,
            Grade: grade,
            Price: price);

    public static TrackedBanana CreateTracked(
        decimal price = 7.50m,
        FarmOrigin origin = FarmOrigin.XFarm) =>
        new(
            BananaId: Guid.NewGuid(),
            FarmOrigin: origin,
            Price: price,
            Reason: PublishReason.Boxed,
            Classification: Ordinary,
            Banana: CreateBanana(price, origin),
            PublishedAt: DateTimeOffset.UnixEpoch);
}
