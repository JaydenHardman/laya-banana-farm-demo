namespace BananaFarm.Contracts;

/// <summary>
/// Why a banana was published to the topic it landed on. Carried on the wire so consumers
/// need not re-derive intent from the routing key alone.
/// </summary>
public enum PublishReason
{
    Golden,
    ThrownAway,
    BoxTimedOut,
    Boxed,
}

/// <summary>
/// A banana published downstream of the factory. Every message on every topic carries at
/// minimum the banana's price and farm of origin, so value can be attributed later
/// (SPEC 6.5).
/// </summary>
public sealed record TrackedBanana(
    Guid BananaId,
    FarmOrigin FarmOrigin,
    decimal Price,
    PublishReason Reason,
    BananaClassification Classification,
    Banana Banana,
    DateTimeOffset PublishedAt);

/// <summary>
/// A box that reached capacity, published to <see cref="Topics.FilledBox"/>.
/// </summary>
/// <param name="TotalPrice">Sum of the prices of every banana in the box.</param>
public sealed record FilledBoxMessage(
    Guid BoxId,
    ClassifiedGrade Grade,
    int RipenessLevel,
    IReadOnlyList<TrackedBanana> Bananas,
    decimal TotalPrice,
    DateTimeOffset OpenedAt,
    DateTimeOffset FilledAt)
{
    /// <summary>Number of bananas in the box.</summary>
    public int Count => Bananas.Count;
}
