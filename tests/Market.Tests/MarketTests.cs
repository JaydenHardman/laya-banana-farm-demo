using System.Collections.Concurrent;
using BananaFarm.Contracts;
using BananaFarm.Market.Ingest;
using BananaFarm.Market.Metrics;
using BananaFarm.Market.Stats;
using Microsoft.Extensions.Logging.Abstractions;

namespace BananaFarm.Market.Tests;

/// <summary>In-memory <see cref="IMarketMetricsStore"/> mirroring the Redis accumulators.</summary>
internal sealed class FakeMetricsStore : IMarketMetricsStore
{
    private readonly ConcurrentDictionary<string, MetricAggregate> _global = new();
    private readonly ConcurrentDictionary<(string, FarmOrigin), MetricAggregate> _byFarm = new();

    public Task RecordGlobalAsync(string metric, double value, CancellationToken cancellationToken)
    {
        _global.AddOrUpdate(
            metric,
            _ => new MetricAggregate(value, 1),
            (_, existing) => new MetricAggregate(existing.Sum + value, existing.Count + 1));

        return Task.CompletedTask;
    }

    public Task RecordFarmAsync(
        string metric,
        FarmOrigin origin,
        double value,
        CancellationToken cancellationToken)
    {
        _byFarm.AddOrUpdate(
            (metric, origin),
            _ => new MetricAggregate(value, 1),
            (_, existing) => new MetricAggregate(existing.Sum + value, existing.Count + 1));

        return Task.CompletedTask;
    }

    public Task<MetricAggregate> GetAsync(string metric, CancellationToken cancellationToken) =>
        Task.FromResult(_global.GetValueOrDefault(metric, MetricAggregate.Empty));

    public Task<IReadOnlyDictionary<FarmOrigin, MetricAggregate>> GetByFarmAsync(
        string metric,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<FarmOrigin, MetricAggregate> result = Enum.GetValues<FarmOrigin>()
            .ToDictionary(
                origin => origin,
                origin => _byFarm.GetValueOrDefault((metric, origin), MetricAggregate.Empty));

        return Task.FromResult(result);
    }
}

internal static class MarketTestData
{
    public static TrackedBanana Banana(decimal price, FarmOrigin origin) => new(
        BananaId: Guid.NewGuid(),
        FarmOrigin: origin,
        Price: price,
        Reason: PublishReason.Boxed,
        Classification: new BananaClassification(ClassifiedGrade.B, 0.9, 2, 0.01),
        Banana: new Banana(
            Guid.NewGuid(), DateTimeOffset.UnixEpoch, origin, 120f, 0.5f, BananaGrade.B, price),
        PublishedAt: DateTimeOffset.UnixEpoch);

    public static FilledBoxMessage Box(params TrackedBanana[] bananas) => new(
        BoxId: Guid.NewGuid(),
        Grade: ClassifiedGrade.B,
        RipenessLevel: 2,
        Bananas: bananas,
        TotalPrice: bananas.Sum(banana => banana.Price),
        OpenedAt: DateTimeOffset.UnixEpoch,
        FilledAt: DateTimeOffset.UnixEpoch);
}

public sealed class StatAggregationTests
{
    [Fact]
    public async Task Average_box_price_reports_the_mean_of_recorded_boxes()
    {
        var store = new FakeMetricsStore();
        await store.RecordGlobalAsync(MarketMetrics.BoxPrice, 100, Cancel.Token);
        await store.RecordGlobalAsync(MarketMetrics.BoxPrice, 150, Cancel.Token);
        await store.RecordGlobalAsync(MarketMetrics.BoxPrice, 200, Cancel.Token);

        var value = await new AverageBoxPriceStat().ComputeAsync(store, Cancel.Token);

        Assert.Equal(150, value.Value);
        Assert.Equal(3, value.SampleCount);
        Assert.Equal("average-box-price", value.Id);
    }

    [Fact]
    public async Task Past_due_loss_reports_the_running_total()
    {
        var store = new FakeMetricsStore();
        await store.RecordAsync(MarketMetrics.PastRipeLoss, FarmOrigin.KindaCrazyNanas, 2.50, Cancel.Token);
        await store.RecordAsync(MarketMetrics.PastRipeLoss, FarmOrigin.TheBananaBoyz, 1.25, Cancel.Token);

        var value = await new PastDueLossStat().ComputeAsync(store, Cancel.Token);

        Assert.Equal(3.75, value.Value);
        Assert.Equal(2, value.SampleCount);
    }

    [Fact]
    public async Task Golden_count_reports_observations_rather_than_their_value()
    {
        var store = new FakeMetricsStore();

        for (var i = 0; i < 7; i++)
        {
            await store.RecordAsync(MarketMetrics.GoldenBananas, FarmOrigin.XFarm, 55.0, Cancel.Token);
        }

        var value = await new GoldenCountStat().ComputeAsync(store, Cancel.Token);

        Assert.Equal(7, value.Value);
    }

    [Fact]
    public async Task A_stat_with_no_observations_reports_zero_rather_than_failing()
    {
        var store = new FakeMetricsStore();

        foreach (var stat in new IStat[]
                 {
                     new AverageBoxPriceStat(), new PastDueLossStat(), new GoldenCountStat(),
                 })
        {
            var value = await stat.ComputeAsync(store, Cancel.Token);

            Assert.Equal(0, value.Value);
            Assert.Equal(0, value.SampleCount);
        }
    }

    [Fact]
    public async Task Per_farm_slices_keep_farms_separate()
    {
        var store = new FakeMetricsStore();
        await store.RecordAsync(MarketMetrics.PastRipeLoss, FarmOrigin.KindaCrazyNanas, 10, Cancel.Token);
        await store.RecordAsync(MarketMetrics.PastRipeLoss, FarmOrigin.TheBananaBoyz, 4, Cancel.Token);

        var byFarm = await new PastDueLossStat().ComputeByFarmAsync(store, Cancel.Token);

        Assert.Equal(10, byFarm[FarmOrigin.KindaCrazyNanas].Value);
        Assert.Equal(4, byFarm[FarmOrigin.TheBananaBoyz].Value);
        Assert.Equal(0, byFarm[FarmOrigin.XFarm].Value);
    }
}

public sealed class StatRegistryTests
{
    private static StatRegistry CreateRegistry() =>
        new([new AverageBoxPriceStat(), new PastDueLossStat(), new GoldenCountStat()]);

    [Fact]
    public void The_registry_exposes_every_registered_stat()
    {
        Assert.Equal(
            ["average-box-price", "golden-count", "past-due-loss"],
            CreateRegistry().Ids);
    }

    [Fact]
    public void Stats_are_resolved_by_id_regardless_of_case()
    {
        Assert.True(CreateRegistry().TryGet("Average-Box-Price", out var stat));
        Assert.Equal("average-box-price", stat.Id);
    }

    [Fact]
    public void An_unknown_id_resolves_to_nothing()
    {
        Assert.False(CreateRegistry().TryGet("bananas-per-fortnight", out _));
    }

    [Fact]
    public void Duplicate_ids_fail_at_startup_rather_than_shadowing_silently()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new StatRegistry([new GoldenCountStat(), new GoldenCountStat()]));

        Assert.Contains("golden-count", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_catalogue_describes_every_stat()
    {
        var descriptors = CreateRegistry().Describe();

        Assert.Equal(3, descriptors.Count);
        Assert.All(descriptors, descriptor =>
        {
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Name));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Unit));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Description));
        });
    }
}

public sealed class MarketRecorderTests
{
    private static MarketRecorder CreateRecorder(FakeMetricsStore store) =>
        new(store, NullLogger<MarketRecorder>.Instance);

    [Fact]
    public async Task A_filled_box_counts_once_globally_at_its_full_value()
    {
        var store = new FakeMetricsStore();

        await CreateRecorder(store).RecordFilledBoxAsync(
            MarketTestData.Box(
                MarketTestData.Banana(5m, FarmOrigin.XFarm),
                MarketTestData.Banana(6m, FarmOrigin.TheBananaBoyz)),
            Cancel.Token);

        var aggregate = await store.GetAsync(MarketMetrics.BoxPrice, Cancel.Token);

        Assert.Equal(11, aggregate.Sum);
        Assert.Equal(1, aggregate.Count);
    }

    [Fact]
    public async Task Each_farm_in_a_box_is_credited_only_with_its_own_share()
    {
        // A box holds bananas from several farms. Crediting the full total to each would
        // inflate every farm's figures, so each gets the sum of its own bananas.
        var store = new FakeMetricsStore();

        await CreateRecorder(store).RecordFilledBoxAsync(
            MarketTestData.Box(
                MarketTestData.Banana(5m, FarmOrigin.XFarm),
                MarketTestData.Banana(3m, FarmOrigin.XFarm),
                MarketTestData.Banana(6m, FarmOrigin.TheBananaBoyz)),
            Cancel.Token);

        var byFarm = await store.GetByFarmAsync(MarketMetrics.BoxPrice, Cancel.Token);

        Assert.Equal(8, byFarm[FarmOrigin.XFarm].Sum);
        Assert.Equal(1, byFarm[FarmOrigin.XFarm].Count);
        Assert.Equal(6, byFarm[FarmOrigin.TheBananaBoyz].Sum);
        Assert.Equal(0, byFarm[FarmOrigin.KindaCrazyNanas].Count);
    }

    [Theory]
    [InlineData(Topics.PastRipe, MarketMetrics.PastRipeLoss)]
    [InlineData(Topics.GoldenBanana, MarketMetrics.GoldenBananas)]
    [InlineData(Topics.TooLongToFill, MarketMetrics.TimedOutBananas)]
    public async Task Each_topic_lands_in_its_own_accumulator(string routingKey, string metric)
    {
        var store = new FakeMetricsStore();
        var body = BananaJson.SerializeToUtf8Bytes(
            MarketTestData.Banana(12.25m, FarmOrigin.KindaCrazyNanas));

        await CreateRecorder(store).RecordAsync(routingKey, body, Cancel.Token);

        var aggregate = await store.GetAsync(metric, Cancel.Token);

        Assert.Equal(12.25, aggregate.Sum);
        Assert.Equal(1, aggregate.Count);
    }

    [Fact]
    public async Task An_unrecognised_routing_key_is_ignored_rather_than_throwing()
    {
        var store = new FakeMetricsStore();

        await CreateRecorder(store).RecordAsync("bananaGossip", new byte[] { 1, 2, 3 }, Cancel.Token);

        Assert.Equal(0, (await store.GetAsync(MarketMetrics.BoxPrice, Cancel.Token)).Count);
    }

    [Fact]
    public async Task A_filled_box_round_trips_through_serialization()
    {
        var store = new FakeMetricsStore();
        var box = MarketTestData.Box(
            MarketTestData.Banana(4m, FarmOrigin.XFarm),
            MarketTestData.Banana(9m, FarmOrigin.KindaCrazyNanas));

        await CreateRecorder(store).RecordAsync(
            Topics.FilledBox,
            BananaJson.SerializeToUtf8Bytes(box),
            Cancel.Token);

        Assert.Equal(13, (await store.GetAsync(MarketMetrics.BoxPrice, Cancel.Token)).Sum);
    }
}
