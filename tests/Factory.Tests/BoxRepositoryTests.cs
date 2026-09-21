using BananaFarm.Contracts;
using BananaFarm.Factory.Boxing;
using Microsoft.Extensions.Options;

namespace BananaFarm.Factory.Tests;

/// <summary>
/// Box semantics: capacity, price totals, isolation between box keys, and expiry.
/// </summary>
/// <remarks>
/// Exercised against <see cref="InMemoryBoxRepository"/>. The Redis implementation satisfies
/// the same <see cref="IBoxRepository"/> contract through Lua scripts and is covered by the
/// end-to-end compose run rather than here, since it needs a live server.
/// </remarks>
public sealed class BoxRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);

    private static IBoxRepository CreateRepository(int capacity = 20) =>
        new InMemoryBoxRepository(Options.Create(new FactoryOptions { BoxCapacity = capacity }));

    private static readonly BoxKey Key = new(ClassifiedGrade.B, 2);

    [Fact]
    public async Task A_box_stays_open_until_it_reaches_capacity()
    {
        var repository = CreateRepository();

        for (var i = 0; i < 19; i++)
        {
            var result = await repository.AddAsync(Key, TestData.CreateTracked(), Now, Cancel.Token);
            Assert.Null(result);
        }

        Assert.Equal(1, await repository.CountOpenAsync(Cancel.Token));
    }

    [Fact]
    public async Task The_twentieth_banana_closes_the_box()
    {
        var repository = CreateRepository();
        FilledBoxMessage? filled = null;

        for (var i = 0; i < 20; i++)
        {
            filled = await repository.AddAsync(Key, TestData.CreateTracked(), Now, Cancel.Token);
        }

        Assert.NotNull(filled);
        Assert.Equal(20, filled.Count);
        Assert.Equal(ClassifiedGrade.B, filled.Grade);
        Assert.Equal(2, filled.RipenessLevel);
        Assert.Equal(0, await repository.CountOpenAsync(Cancel.Token));
    }

    [Fact]
    public async Task A_filled_box_carries_the_sum_of_its_bananas_prices()
    {
        var repository = CreateRepository(capacity: 3);

        await repository.AddAsync(Key, TestData.CreateTracked(price: 5.25m), Now, Cancel.Token);
        await repository.AddAsync(Key, TestData.CreateTracked(price: 7.50m), Now, Cancel.Token);
        var filled = await repository.AddAsync(Key, TestData.CreateTracked(price: 1.25m), Now, Cancel.Token);

        Assert.NotNull(filled);
        Assert.Equal(14.00m, filled.TotalPrice);
    }

    [Fact]
    public async Task A_new_box_opens_once_the_previous_one_is_full()
    {
        var repository = CreateRepository(capacity: 2);

        await repository.AddAsync(Key, TestData.CreateTracked(), Now, Cancel.Token);
        var filled = await repository.AddAsync(Key, TestData.CreateTracked(), Now, Cancel.Token);
        Assert.NotNull(filled);

        // The next banana has nowhere to go, so a fresh box must open for it.
        var next = await repository.AddAsync(Key, TestData.CreateTracked(), Now, Cancel.Token);

        Assert.Null(next);
        Assert.Equal(1, await repository.CountOpenAsync(Cancel.Token));
    }

    [Fact]
    public async Task Boxes_with_different_keys_fill_independently()
    {
        var repository = CreateRepository(capacity: 2);
        var otherKey = new BoxKey(ClassifiedGrade.A, 1);

        await repository.AddAsync(Key, TestData.CreateTracked(), Now, Cancel.Token);
        var result = await repository.AddAsync(otherKey, TestData.CreateTracked(), Now, Cancel.Token);

        Assert.Null(result);
        Assert.Equal(2, await repository.CountOpenAsync(Cancel.Token));
    }

    [Fact]
    public async Task Each_filled_box_gets_its_own_identity()
    {
        var repository = CreateRepository(capacity: 1);

        var first = await repository.AddAsync(Key, TestData.CreateTracked(), Now, Cancel.Token);
        var second = await repository.AddAsync(Key, TestData.CreateTracked(), Now, Cancel.Token);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first.BoxId, second.BoxId);
    }

    [Fact]
    public async Task A_box_is_expired_against_the_time_its_first_banana_arrived()
    {
        var repository = CreateRepository();

        await repository.AddAsync(Key, TestData.CreateTracked(), Now, Cancel.Token);

        // A later banana joins the same box but must not extend its deadline.
        await repository.AddAsync(Key, TestData.CreateTracked(), Now.AddSeconds(9), Cancel.Token);

        var expired = await repository.RemoveExpiredAsync(Now, Cancel.Token);

        Assert.Single(expired);
        Assert.Equal(Now, expired[0].OpenedAt);
        Assert.Equal(2, expired[0].Bananas.Count);
        Assert.Equal(Key, expired[0].Key);
    }

    [Fact]
    public async Task A_box_opened_after_the_cutoff_survives_the_sweep()
    {
        var repository = CreateRepository();

        await repository.AddAsync(Key, TestData.CreateTracked(), Now, Cancel.Token);

        var expired = await repository.RemoveExpiredAsync(Now.AddSeconds(-1), Cancel.Token);

        Assert.Empty(expired);
        Assert.Equal(1, await repository.CountOpenAsync(Cancel.Token));
    }

    [Fact]
    public async Task Expired_boxes_are_removed_and_never_returned_twice()
    {
        var repository = CreateRepository();

        await repository.AddAsync(Key, TestData.CreateTracked(), Now, Cancel.Token);

        Assert.Single(await repository.RemoveExpiredAsync(Now, Cancel.Token));
        Assert.Empty(await repository.RemoveExpiredAsync(Now, Cancel.Token));
        Assert.Equal(0, await repository.CountOpenAsync(Cancel.Token));
    }

    [Fact]
    public async Task Concurrent_adds_produce_exactly_full_boxes_and_lose_nothing()
    {
        var repository = CreateRepository(capacity: 20);
        var filled = new List<FilledBoxMessage>();
        var gate = new Lock();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, 2_000),
            async (_, token) =>
            {
                var result = await repository.AddAsync(Key, TestData.CreateTracked(), Now, token);

                if (result is not null)
                {
                    lock (gate)
                    {
                        filled.Add(result);
                    }
                }
            });

        Assert.Equal(100, filled.Count);
        Assert.All(filled, box => Assert.Equal(20, box.Count));
        Assert.Equal(0, await repository.CountOpenAsync(Cancel.Token));
    }
}
