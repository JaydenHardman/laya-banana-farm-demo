using System.ComponentModel.DataAnnotations;

namespace BananaFarm.Market;

/// <summary>Settings for the market service.</summary>
public sealed class MarketOptions
{
    public const string SectionName = "Market";

    /// <summary>StackExchange.Redis connection string.</summary>
    [Required]
    public string RedisConfiguration { get; set; } = "redis:6379";

    /// <summary>Prefix applied to every key this service owns.</summary>
    public string KeyPrefix { get; set; } = "bananafarm";

    /// <summary>
    /// Whether to also consume the raw <c>bananaProduction</c> stream. Off by default: no
    /// market metric needs it, and binding it would route the farm's full throughput through
    /// this service for nothing.
    /// </summary>
    public bool ConsumeRawProduction { get; set; }

    /// <summary>Default push interval for a WebSocket subscriber that does not specify one.</summary>
    [Range(1, 300)]
    public int DefaultLiveIntervalSeconds { get; set; } = 5;

    /// <summary>Smallest push interval a WebSocket subscriber may request.</summary>
    [Range(1, 300)]
    public int MinLiveIntervalSeconds { get; set; } = 1;

    /// <summary>Largest push interval a WebSocket subscriber may request.</summary>
    [Range(1, 3_600)]
    public int MaxLiveIntervalSeconds { get; set; } = 300;
}
