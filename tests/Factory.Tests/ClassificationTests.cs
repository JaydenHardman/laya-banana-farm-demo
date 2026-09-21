using BananaFarm.Contracts;
using BananaFarm.Factory.Classification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BananaFarm.Factory.Tests;

public sealed class ClassificationBucketTests
{
    private static readonly FactoryOptions Options = new();

    [Fact]
    public void Bananas_within_the_same_bucket_share_a_key()
    {
        var first = TestData.CreateBanana(ripeness: 0.50f, weight: 120f);
        var second = TestData.CreateBanana(ripeness: 0.54f, weight: 128f);

        Assert.Equal(
            ClassificationBucket.KeyFor(first, Options),
            ClassificationBucket.KeyFor(second, Options));
    }

    [Fact]
    public void Crossing_a_ripeness_boundary_changes_the_key()
    {
        var first = TestData.CreateBanana(ripeness: 0.54f);
        var second = TestData.CreateBanana(ripeness: 0.56f);

        Assert.NotEqual(
            ClassificationBucket.KeyFor(first, Options),
            ClassificationBucket.KeyFor(second, Options));
    }

    [Fact]
    public void Crossing_a_weight_boundary_changes_the_key()
    {
        var first = TestData.CreateBanana(weight: 119f);
        var second = TestData.CreateBanana(weight: 121f);

        Assert.NotEqual(
            ClassificationBucket.KeyFor(first, Options),
            ClassificationBucket.KeyFor(second, Options));
    }

    [Fact]
    public void Farm_and_grade_separate_otherwise_identical_bananas()
    {
        var baseline = TestData.CreateBanana(origin: FarmOrigin.XFarm, grade: BananaGrade.A);

        Assert.NotEqual(
            ClassificationBucket.KeyFor(baseline, Options),
            ClassificationBucket.KeyFor(
                TestData.CreateBanana(origin: FarmOrigin.TheBananaBoyz, grade: BananaGrade.A),
                Options));

        Assert.NotEqual(
            ClassificationBucket.KeyFor(baseline, Options),
            ClassificationBucket.KeyFor(
                TestData.CreateBanana(origin: FarmOrigin.XFarm, grade: BananaGrade.C),
                Options));
    }

    [Fact]
    public void The_key_is_stable_across_calls()
    {
        var banana = TestData.CreateBanana();

        Assert.Equal(
            ClassificationBucket.KeyFor(banana, Options),
            ClassificationBucket.KeyFor(banana, Options));
    }
}

public sealed class ClassificationPipelineTests
{
    private static ClassificationPipeline CreatePipeline(
        FakeCache cache,
        IClassificationClient client,
        FactoryOptions? options = null) =>
        new(
            cache,
            client,
            Options.Create(options ?? new FactoryOptions { ClassifierBatchDelayMilliseconds = 10 }),
            NullLogger<ClassificationPipeline>.Instance);

    [Fact]
    public async Task A_cache_miss_reaches_the_model()
    {
        var cache = new FakeCache();
        var client = new FakeClassificationClient();
        var pipeline = CreatePipeline(cache, client);

        await pipeline.StartAsync(Cancel.Token);

        var result = await pipeline.ClassifyAsync(TestData.CreateBanana(), Cancel.Token);

        Assert.Equal(TestData.Ordinary, result);
        Assert.Equal(1, client.BananasClassified);
        Assert.Equal(1, pipeline.CacheMisses);

        await pipeline.StopAsync(Cancel.Token);
    }

    [Fact]
    public async Task A_classified_bucket_is_served_from_cache_next_time()
    {
        var cache = new FakeCache();
        var client = new FakeClassificationClient();
        var pipeline = CreatePipeline(cache, client);

        await pipeline.StartAsync(Cancel.Token);

        var banana = TestData.CreateBanana();
        await pipeline.ClassifyAsync(banana, Cancel.Token);
        await pipeline.ClassifyAsync(banana, Cancel.Token);

        Assert.Equal(1, client.BananasClassified);
        Assert.Equal(1, pipeline.CacheHits);
        Assert.Equal(1, pipeline.CacheMisses);

        await pipeline.StopAsync(Cancel.Token);
    }

    [Fact]
    public async Task Simultaneous_misses_on_one_bucket_make_a_single_model_call()
    {
        // This is what keeps a 200/s burst of near-identical bananas from becoming 200
        // inferences before the first result lands in the cache.
        var cache = new FakeCache();
        var client = new FakeClassificationClient();
        var pipeline = CreatePipeline(cache, client);

        await pipeline.StartAsync(Cancel.Token);

        var banana = TestData.CreateBanana();

        var results = await Task.WhenAll(
            Enumerable.Range(0, 50).Select(_ => pipeline.ClassifyAsync(banana, Cancel.Token)));

        Assert.All(results, result => Assert.Equal(TestData.Ordinary, result));
        Assert.Equal(1, client.BananasClassified);

        await pipeline.StopAsync(Cancel.Token);
    }

    [Fact]
    public async Task Distinct_buckets_are_dispatched_together_as_one_batch()
    {
        var cache = new FakeCache();
        var client = new FakeClassificationClient();
        var pipeline = CreatePipeline(cache, client);

        await pipeline.StartAsync(Cancel.Token);

        var bananas = Enumerable.Range(0, 8)
            .Select(i => TestData.CreateBanana(weight: 80f + (i * 15f)))
            .ToList();

        await Task.WhenAll(bananas.Select(banana => pipeline.ClassifyAsync(banana, Cancel.Token)));

        Assert.Equal(8, client.BananasClassified);
        Assert.True(
            client.BatchSizes.Count < 8,
            $"Expected the misses to be batched, but they were sent as {client.BatchSizes.Count} calls.");

        await pipeline.StopAsync(Cancel.Token);
    }

    [Fact]
    public async Task A_model_failure_surfaces_to_the_caller()
    {
        var cache = new FakeCache();
        var pipeline = CreatePipeline(cache, new ThrowingClient());

        await pipeline.StartAsync(Cancel.Token);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.ClassifyAsync(TestData.CreateBanana(), Cancel.Token));

        await pipeline.StopAsync(Cancel.Token);
    }

    private sealed class ThrowingClient : IClassificationClient
    {
        public Task<IReadOnlyList<BananaClassification>> ClassifyAsync(
            IReadOnlyList<Banana> bananas,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("classifier is down");
    }
}
