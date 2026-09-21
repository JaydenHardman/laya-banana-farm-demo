using BananaFarm.Farm.Generation;
using BananaFarm.Farm.Production;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BananaFarm.Farm.Tests;

public sealed class ProductionControlTests
{
    private static ProductionControl Create(double startingRate = 8) =>
        new(
            Options.Create(new FarmOptions { BaseRatePerSecond = startingRate }),
            NullLogger<ProductionControl>.Instance);

    [Fact]
    public void The_farm_starts_producing_at_its_configured_rate()
    {
        var control = Create(startingRate: 12);

        Assert.True(control.Current.Running);
        Assert.Equal(12, control.Current.BaseRatePerSecond);
    }

    [Fact]
    public void Production_can_be_stopped_and_started_again()
    {
        var control = Create();

        Assert.True(control.Apply(running: false, ratePerSecond: null).Accepted);
        Assert.False(control.Current.Running);

        Assert.True(control.Apply(running: true, ratePerSecond: null).Accepted);
        Assert.True(control.Current.Running);
    }

    [Fact]
    public void Stopping_leaves_the_rate_untouched_so_it_is_restored_on_restart()
    {
        var control = Create(startingRate: 42);

        control.Apply(running: false, ratePerSecond: null);

        Assert.Equal(42, control.Current.BaseRatePerSecond);
    }

    [Fact]
    public void The_rate_can_be_changed_without_restating_whether_it_is_running()
    {
        var control = Create();
        control.Apply(running: false, ratePerSecond: null);

        control.Apply(running: null, ratePerSecond: 25);

        Assert.Equal(25, control.Current.BaseRatePerSecond);
        Assert.False(control.Current.Running);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    [InlineData(ProductionControl.MaxRatePerSecond + 1)]
    public void An_out_of_range_rate_is_refused_and_changes_nothing(double rate)
    {
        var control = Create(startingRate: 8);

        var result = control.Apply(running: null, ratePerSecond: rate);

        Assert.False(result.Accepted);
        Assert.NotNull(result.Error);
        Assert.Equal(8, control.Current.BaseRatePerSecond);
    }

    [Fact]
    public void A_refused_change_does_not_apply_the_running_flag_either()
    {
        // The request is rejected whole; a caller must not get a half-applied change.
        var control = Create();

        control.Apply(running: false, ratePerSecond: -1);

        Assert.True(control.Current.Running);
    }

    [Theory]
    [InlineData(ProductionControl.MinRatePerSecond)]
    [InlineData(200)]
    [InlineData(ProductionControl.MaxRatePerSecond)]
    public void Rates_at_and_inside_the_bounds_are_accepted(double rate)
    {
        var control = Create();

        Assert.True(control.Apply(running: null, ratePerSecond: rate).Accepted);
        Assert.Equal(rate, control.Current.BaseRatePerSecond);
    }

    [Fact]
    public void An_empty_change_is_accepted_and_reports_the_current_state()
    {
        var control = Create(startingRate: 8);

        var result = control.Apply(running: null, ratePerSecond: null);

        Assert.True(result.Accepted);
        Assert.True(result.State.Running);
        Assert.Equal(8, result.State.BaseRatePerSecond);
    }

    [Fact]
    public void Concurrent_changes_leave_a_consistent_state()
    {
        var control = Create();

        Parallel.For(0, 2_000, i =>
        {
            control.Apply(running: i % 2 == 0, ratePerSecond: 5 + (i % 10));
            _ = control.Current;
        });

        var final = control.Current;
        Assert.InRange(final.BaseRatePerSecond, 5, 14);
    }
}
