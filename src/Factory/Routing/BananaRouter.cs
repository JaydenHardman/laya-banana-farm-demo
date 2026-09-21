using BananaFarm.Contracts;

namespace BananaFarm.Factory.Routing;

/// <summary>Where a classified banana goes.</summary>
public enum BananaDestination
{
    /// <summary>Discard: published alone to <c>pastRipe</c>.</summary>
    PastRipe,

    /// <summary>Published alone to <c>goldenBanana</c>. Never boxed.</summary>
    GoldenBanana,

    /// <summary>Added to a box of similar bananas.</summary>
    Box,
}

/// <summary>Applies the routing rules from SPEC 6.3.</summary>
public static class BananaRouter
{
    /// <summary>
    /// Decide where a banana goes from the classifier's verdict.
    /// </summary>
    /// <remarks>
    /// Order matters and is load-bearing: throw-away is checked <em>before</em> golden, so a
    /// golden banana that should be discarded goes to <c>pastRipe</c> rather than
    /// <c>goldenBanana</c>. The brief calls this out explicitly.
    /// </remarks>
    public static BananaDestination Decide(BananaClassification classification)
    {
        ArgumentNullException.ThrowIfNull(classification);

        if (classification.ShouldThrowAway)
        {
            return BananaDestination.PastRipe;
        }

        if (classification.Grade == ClassifiedGrade.Golden)
        {
            return BananaDestination.GoldenBanana;
        }

        return BananaDestination.Box;
    }

    /// <summary>The routing key a destination publishes to.</summary>
    public static string TopicFor(BananaDestination destination) => destination switch
    {
        BananaDestination.PastRipe => Topics.PastRipe,
        BananaDestination.GoldenBanana => Topics.GoldenBanana,
        BananaDestination.Box => Topics.FilledBox,
        _ => throw new ArgumentOutOfRangeException(nameof(destination), destination, null),
    };

    /// <summary>Why a banana was published, given where it was sent.</summary>
    public static PublishReason ReasonFor(BananaDestination destination) => destination switch
    {
        BananaDestination.PastRipe => PublishReason.ThrownAway,
        BananaDestination.GoldenBanana => PublishReason.Golden,
        BananaDestination.Box => PublishReason.Boxed,
        _ => throw new ArgumentOutOfRangeException(nameof(destination), destination, null),
    };
}
