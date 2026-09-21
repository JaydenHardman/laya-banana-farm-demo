namespace BananaFarm.Contracts;

/// <summary>
/// The classification model's verdict on a banana. These answers — not the banana's own
/// harvest-time fields — drive all routing and boxing decisions (SPEC 6.2).
/// </summary>
/// <param name="Grade">Grade chosen by the model.</param>
/// <param name="GradeConfidence">Model confidence in <paramref name="Grade"/>, 0..1.</param>
/// <param name="RipenessLevel">Ordinal ripeness bucket returned by the score primitive.</param>
/// <param name="ThrowAway">Calibrated probability the banana should be discarded, 0..1.</param>
public sealed record BananaClassification(
    ClassifiedGrade Grade,
    double GradeConfidence,
    int RipenessLevel,
    double ThrowAway)
{
    /// <summary>
    /// Probability above which <see cref="ThrowAway"/> is treated as a yes. The model returns
    /// a calibrated probability rather than a boolean, so the cut-off belongs to the caller.
    /// </summary>
    public const double ThrowAwayThreshold = 0.5;

    /// <summary>Whether this banana should be discarded rather than boxed.</summary>
    public bool ShouldThrowAway => ThrowAway >= ThrowAwayThreshold;
}
