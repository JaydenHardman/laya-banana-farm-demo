using System.Text.Json.Serialization;
using BananaFarm.Market.Stats;

namespace BananaFarm.Market.Live;

/// <summary>The frame a client sends after connecting to <c>/stats/live</c>.</summary>
/// <param name="Subscribe">Stat ids to receive. Null or empty subscribes to every stat.</param>
/// <param name="IntervalSeconds">
/// How often to push. Null uses the server default; out-of-range values are clamped.
/// </param>
public sealed record SubscribeFrame(
    [property: JsonPropertyName("subscribe")] IReadOnlyList<string>? Subscribe,
    [property: JsonPropertyName("intervalSeconds")] int? IntervalSeconds);

/// <summary>A snapshot pushed to a subscriber.</summary>
public sealed record SnapshotFrame(
    [property: JsonPropertyName("asOf")] DateTimeOffset AsOf,
    [property: JsonPropertyName("stats")] IReadOnlyDictionary<string, StatValue> Stats);

/// <summary>Sent when a subscription cannot be honoured.</summary>
public sealed record ErrorFrame(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("validStats")] IReadOnlyList<string> ValidStats);

/// <summary>Sent once a subscription is accepted, echoing what the server will push.</summary>
public sealed record SubscribedFrame(
    [property: JsonPropertyName("subscribed")] IReadOnlyList<string> Subscribed,
    [property: JsonPropertyName("intervalSeconds")] int IntervalSeconds);
