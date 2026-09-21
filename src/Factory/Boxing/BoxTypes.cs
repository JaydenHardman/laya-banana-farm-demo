using BananaFarm.Contracts;

namespace BananaFarm.Factory.Boxing;

/// <summary>
/// Identity of a box. Bananas are grouped by <em>similar</em> characteristics, not identical
/// ones: the classifier's grade plus its ordinal ripeness level.
/// </summary>
public readonly record struct BoxKey(ClassifiedGrade Grade, int RipenessLevel)
{
    /// <summary>Stable string form, used as part of the storage key.</summary>
    public override string ToString() => $"{Grade}:{RipenessLevel}";

    /// <summary>The box a classification belongs in.</summary>
    public static BoxKey From(BananaClassification classification) =>
        new(classification.Grade, classification.RipenessLevel);
}

/// <summary>A box removed because it did not fill within the timeout.</summary>
public sealed record ExpiredBox(
    Guid BoxId,
    BoxKey Key,
    DateTimeOffset OpenedAt,
    IReadOnlyList<TrackedBanana> Bananas);

/// <summary>
/// Stores the boxes that are currently open.
/// </summary>
/// <remarks>
/// Implementations must make "append a banana, and report the box if that append filled it"
/// a single indivisible operation. Several consumer threads add to the same box
/// concurrently, and a read-modify-write across that would over- or under-fill boxes.
/// </remarks>
public interface IBoxRepository
{
    /// <summary>
    /// Add <paramref name="banana"/> to the open box for <paramref name="key"/>, opening one
    /// if none exists.
    /// </summary>
    /// <returns>
    /// The completed box if this banana filled it, otherwise <see langword="null"/>. A
    /// returned box is already removed from storage and will not be returned again.
    /// </returns>
    Task<FilledBoxMessage?> AddAsync(
        BoxKey key,
        TrackedBanana banana,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Remove and return every box opened at or before <paramref name="cutoff"/>.
    /// </summary>
    Task<IReadOnlyList<ExpiredBox>> RemoveExpiredAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken);

    /// <summary>Number of boxes currently open. Diagnostics only.</summary>
    Task<int> CountOpenAsync(CancellationToken cancellationToken);
}
