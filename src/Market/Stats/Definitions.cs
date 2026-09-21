using BananaFarm.Market.Metrics;

namespace BananaFarm.Market.Stats;

/// <summary>Mean value of a filled box of bananas.</summary>
public sealed class AverageBoxPriceStat : MetricStat
{
    public override string Id => "average-box-price";

    public override string Name => "Average box price";

    public override string Unit => "currency";

    public override string Description =>
        "Mean total price of a filled box of 20 bananas.";

    protected override string Metric => MarketMetrics.BoxPrice;

    protected override StatAggregation Aggregation => StatAggregation.Mean;
}

/// <summary>Value written off because bananas went past ripe.</summary>
public sealed class PastDueLossStat : MetricStat
{
    public override string Id => "past-due-loss";

    public override string Name => "Past due loss";

    public override string Unit => "currency";

    public override string Description =>
        "Total price of every banana discarded for being past ripe.";

    protected override string Metric => MarketMetrics.PastRipeLoss;

    protected override StatAggregation Aggregation => StatAggregation.Total;
}

/// <summary>How many golden bananas have been produced.</summary>
public sealed class GoldenCountStat : MetricStat
{
    public override string Id => "golden-count";

    public override string Name => "Golden bananas";

    public override string Unit => "bananas";

    public override string Description =>
        "Number of golden bananas produced since startup.";

    protected override string Metric => MarketMetrics.GoldenBananas;

    protected override StatAggregation Aggregation => StatAggregation.Count;
}
