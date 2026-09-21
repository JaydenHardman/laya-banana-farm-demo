using System.ComponentModel.DataAnnotations;
using BananaFarm.Contracts;

namespace BananaFarm.Farm.Generation;

/// <summary>
/// Everything about how bananas are generated. Every distribution the brief left
/// unspecified is a default here rather than a constant in code (SPEC 4.3).
/// </summary>
public sealed class FarmOptions
{
    public const string SectionName = "Farm";

    /// <summary>
    /// Mean bananas per second, before wave multipliers are applied.
    /// </summary>
    /// <remarks>
    /// The design target is 200/s, but the measured end-to-end ceiling on CPU is roughly
    /// 9/s: the classification model sustains about 3 inferences per second, and the bucket
    /// cache cannot absorb the remainder fast enough (SPEC 6.1). The default is set to a
    /// rate the whole stack actually sustains, so the system reaches steady state instead of
    /// growing an unbounded queue. Raise it when the model has a GPU.
    /// </remarks>
    [Range(0.1, 100_000)]
    public double BaseRatePerSecond { get; set; } = 8;

    /// <summary>Probability any given banana is golden. 1 in 1000.</summary>
    [Range(0, 1)]
    public double GoldenProbability { get; set; } = 0.001;

    /// <summary>Probability any given banana exceeds <see cref="OverripeThreshold"/>.</summary>
    [Range(0, 1)]
    public double OverripeProbability { get; set; } = 0.035;

    /// <summary>Ripeness above which a banana counts as over-ripe and is priced as waste.</summary>
    [Range(0, 1)]
    public double OverripeThreshold { get; set; } = 0.75;

    /// <summary>Relative likelihood of each farm producing a banana. Normalised at startup.</summary>
    public Dictionary<FarmOrigin, double> OriginWeights { get; set; } = new()
    {
        [FarmOrigin.XFarm] = 1,
        [FarmOrigin.TheBananaBoyz] = 1,
        [FarmOrigin.KindaCrazyNanas] = 1,
    };

    /// <summary>
    /// How the over-ripe population is split between farms. XFarm never produces an over-ripe
    /// banana; TheBananaBoyz account for 20% of them; the rest belong to KindaCrazyNanas.
    /// </summary>
    public Dictionary<FarmOrigin, double> OverripeShare { get; set; } = new()
    {
        [FarmOrigin.XFarm] = 0.0,
        [FarmOrigin.TheBananaBoyz] = 0.2,
        [FarmOrigin.KindaCrazyNanas] = 0.8,
    };

    /// <summary>Relative grade likelihoods for bananas that did not come out golden.</summary>
    public Dictionary<BananaGrade, double> GradeWeights { get; set; } = new()
    {
        [BananaGrade.A] = 0.30,
        [BananaGrade.B] = 0.45,
        [BananaGrade.C] = 0.25,
    };

    /// <summary>Mean banana weight in grams.</summary>
    [Range(1, 10_000)]
    public double WeightMeanGrams { get; set; } = 120;

    /// <summary>Standard deviation of banana weight in grams.</summary>
    [Range(0, 10_000)]
    public double WeightStdDevGrams { get; set; } = 18;

    /// <summary>Lower clamp on generated weight.</summary>
    [Range(1, 10_000)]
    public double WeightMinGrams { get; set; } = 70;

    /// <summary>Upper clamp on generated weight.</summary>
    [Range(1, 10_000)]
    public double WeightMaxGrams { get; set; } = 200;

    /// <summary>Ripeness value the triangular "normal ripeness" distribution peaks at.</summary>
    [Range(0, 1)]
    public double IdealRipeness { get; set; } = 0.5;

    /// <summary>How often the generator wakes to emit a batch, in milliseconds.</summary>
    [Range(5, 5_000)]
    public int TickMilliseconds { get; set; } = 50;
}
