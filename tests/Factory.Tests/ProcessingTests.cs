using BananaFarm.Contracts;
using BananaFarm.Factory.Boxing;
using BananaFarm.Factory.Classification;
using BananaFarm.Factory.Processing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BananaFarm.Factory.Tests;

public sealed class BananaProcessorTests
{
    private sealed class FixedClassifier : IBananaClassifier
    {
        private readonly BananaClassification _classification;

        public FixedClassifier(BananaClassification classification) =>
            _classification = classification;

        public Task<BananaClassification> ClassifyAsync(
            Banana banana,
            CancellationToken cancellationToken) => Task.FromResult(_classification);
    }

    private static (BananaProcessor Processor, FakePublisher Publisher) Create(
        BananaClassification classification,
        int boxCapacity = 20)
    {
        var publisher = new FakePublisher();
        var options = Options.Create(new FactoryOptions { BoxCapacity = boxCapacity });

        var processor = new BananaProcessor(
            new FixedClassifier(classification),
            new InMemoryBoxRepository(options),
            publisher,
            new FakeTimeProvider(),
            NullLogger<BananaProcessor>.Instance);

        return (processor, publisher);
    }

    [Fact]
    public async Task A_golden_banana_is_published_alone_and_never_boxed()
    {
        var (processor, publisher) = Create(TestData.Golden);

        await processor.ProcessAsync(TestData.CreateBanana(), Cancel.Token);

        var golden = publisher.MessagesOn<TrackedBanana>(Topics.GoldenBanana);

        Assert.Single(golden);
        Assert.Equal(PublishReason.Golden, golden[0].Reason);
        Assert.Empty(publisher.MessagesOn<FilledBoxMessage>(Topics.FilledBox));
    }

    [Fact]
    public async Task A_banana_marked_for_disposal_goes_to_past_ripe()
    {
        var (processor, publisher) = Create(TestData.Rotten);

        await processor.ProcessAsync(TestData.CreateBanana(), Cancel.Token);

        var discarded = publisher.MessagesOn<TrackedBanana>(Topics.PastRipe);

        Assert.Single(discarded);
        Assert.Equal(PublishReason.ThrownAway, discarded[0].Reason);
    }

    [Fact]
    public async Task A_golden_banana_that_should_be_discarded_goes_to_past_ripe_not_golden()
    {
        var (processor, publisher) = Create(TestData.GoldenAndRotten);

        await processor.ProcessAsync(TestData.CreateBanana(), Cancel.Token);

        Assert.Single(publisher.MessagesOn<TrackedBanana>(Topics.PastRipe));
        Assert.Empty(publisher.MessagesOn<TrackedBanana>(Topics.GoldenBanana));
    }

    [Fact]
    public async Task Ordinary_bananas_publish_nothing_until_a_box_fills()
    {
        var (processor, publisher) = Create(TestData.Ordinary);

        for (var i = 0; i < 19; i++)
        {
            await processor.ProcessAsync(TestData.CreateBanana(), Cancel.Token);
        }

        Assert.Empty(publisher.Published);

        await processor.ProcessAsync(TestData.CreateBanana(), Cancel.Token);

        Assert.Single(publisher.MessagesOn<FilledBoxMessage>(Topics.FilledBox));
    }

    [Fact]
    public async Task A_published_box_totals_the_prices_of_the_bananas_it_holds()
    {
        var (processor, publisher) = Create(TestData.Ordinary, boxCapacity: 4);

        foreach (var price in new[] { 5.00m, 6.25m, 7.75m, 9.00m })
        {
            await processor.ProcessAsync(TestData.CreateBanana(price: price), Cancel.Token);
        }

        var box = Assert.Single(publisher.MessagesOn<FilledBoxMessage>(Topics.FilledBox));

        Assert.Equal(28.00m, box.TotalPrice);
        Assert.Equal(4, box.Count);
    }

    [Fact]
    public async Task Every_published_banana_carries_its_price_and_farm_of_origin()
    {
        var (processor, publisher) = Create(TestData.Golden);

        await processor.ProcessAsync(
            TestData.CreateBanana(price: 42.50m, origin: FarmOrigin.KindaCrazyNanas),
            Cancel.Token);

        var published = Assert.Single(publisher.MessagesOn<TrackedBanana>(Topics.GoldenBanana));

        Assert.Equal(42.50m, published.Price);
        Assert.Equal(FarmOrigin.KindaCrazyNanas, published.FarmOrigin);
    }
}

public sealed class BoxTimeoutSweeperTests
{
    [Fact]
    public async Task A_box_that_does_not_fill_within_the_timeout_is_released()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero));
        var options = Options.Create(new FactoryOptions
        {
            BoxCapacity = 20,
            BoxTimeoutSeconds = 10,
            SweepIntervalMilliseconds = 500,
        });

        var boxes = new InMemoryBoxRepository(options);
        var publisher = new FakePublisher();

        await boxes.AddAsync(
            new BoxKey(ClassifiedGrade.B, 2),
            TestData.CreateTracked(price: 6m),
            time.GetUtcNow(),
            Cancel.Token);

        var sweeper = new BoxTimeoutSweeper(
            boxes,
            publisher,
            options,
            time,
            NullLogger<BoxTimeoutSweeper>.Instance);

        await sweeper.StartAsync(Cancel.Token);

        // Nine seconds in, the box is still within its window.
        time.Advance(TimeSpan.FromSeconds(9));
        await Task.Delay(50, Cancel.Token);
        Assert.Empty(publisher.Published);

        // Past ten, it must be flushed.
        time.Advance(TimeSpan.FromSeconds(2));
        await Task.Delay(50, Cancel.Token);

        var released = publisher.MessagesOn<TrackedBanana>(Topics.TooLongToFill);

        Assert.Single(released);
        Assert.Equal(PublishReason.BoxTimedOut, released[0].Reason);
        Assert.Equal(6m, released[0].Price);

        await sweeper.StopAsync(Cancel.Token);
    }
}
