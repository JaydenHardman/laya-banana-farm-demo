using System.ComponentModel.DataAnnotations;

namespace BananaFarm.Factory;

/// <summary>Redis connection settings.</summary>
/// <remarks>
/// Redis is a stand-in for a durable store. Everything written through it sits behind an
/// interface (SPEC 9), so replacing it is a DI registration change.
/// </remarks>
public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>StackExchange.Redis connection string.</summary>
    [Required]
    public string Configuration { get; set; } = "redis:6379";

    /// <summary>Prefix applied to every key this service owns.</summary>
    public string KeyPrefix { get; set; } = "bananafarm";
}
