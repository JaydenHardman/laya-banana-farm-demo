using BananaFarm.Contracts;

namespace BananaFarm.Farm.Generation;

/// <summary>
/// Derives, from the global targets in <see cref="FarmOptions"/>, the probability that a
/// banana is over-ripe <em>given</em> which farm produced it.
/// </summary>
/// <remarks>
/// <para>
/// The brief states four constraints that must hold at once: 3.5% of all bananas are
/// over-ripe, XFarm never produces one, TheBananaBoyz account for 20% of the over-ripe
/// population, and KindaCrazyNanas account for the rest. Those are statements about the
/// <em>global</em> population, but generation picks a farm first — so what the generator
/// actually needs is a conditional probability per farm.
/// </para>
/// <para>
/// By Bayes: <c>P(overripe | origin) = P(overripe) x share(origin) / P(origin)</c>.
/// Deriving this rather than hardcoding the three numbers means the invariants still hold
/// when origin weights are reconfigured.
/// </para>
/// </remarks>
public sealed class OriginRipenessModel
{
    private readonly IReadOnlyDictionary<FarmOrigin, double> _originProbability;
    private readonly IReadOnlyDictionary<FarmOrigin, double> _overripeGivenOrigin;
    private readonly (FarmOrigin Origin, double CumulativeProbability)[] _originCdf;

    public OriginRipenessModel(FarmOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _originProbability = Normalise(options.OriginWeights, nameof(FarmOptions.OriginWeights));
        var shares = Normalise(options.OverripeShare, nameof(FarmOptions.OverripeShare));

        var conditionals = new Dictionary<FarmOrigin, double>();
        foreach (var (origin, originProbability) in _originProbability)
        {
            var share = shares.GetValueOrDefault(origin);

            if (originProbability <= 0)
            {
                // A farm that never produces bananas cannot carry any of the over-ripe pool.
                if (share > 0)
                {
                    throw new InvalidOperationException(
                        $"{origin} is assigned {share:P1} of the over-ripe population but has an " +
                        "origin weight of zero, so it produces no bananas at all. Give it a " +
                        $"non-zero {nameof(FarmOptions.OriginWeights)} entry or move its " +
                        $"{nameof(FarmOptions.OverripeShare)} to another farm.");
                }

                conditionals[origin] = 0;
                continue;
            }

            var conditional = options.OverripeProbability * share / originProbability;

            if (conditional > 1)
            {
                throw new InvalidOperationException(
                    $"Configuration is unsatisfiable: {origin} would need {conditional:P1} of its " +
                    $"bananas to be over-ripe to supply {share:P1} of a population that is " +
                    $"{options.OverripeProbability:P1} over-ripe overall, while producing only " +
                    $"{originProbability:P1} of all bananas. Raise its " +
                    $"{nameof(FarmOptions.OriginWeights)} entry, lower its " +
                    $"{nameof(FarmOptions.OverripeShare)}, or lower " +
                    $"{nameof(FarmOptions.OverripeProbability)}.");
            }

            conditionals[origin] = conditional;
        }

        _overripeGivenOrigin = conditionals;

        var cumulative = 0.0;
        _originCdf = [.. _originProbability.Select(pair =>
        {
            cumulative += pair.Value;
            return (pair.Key, cumulative);
        })];
    }

    /// <summary>Probability that a banana comes from <paramref name="origin"/>.</summary>
    public double ProbabilityOf(FarmOrigin origin) => _originProbability.GetValueOrDefault(origin);

    /// <summary>Probability a banana is over-ripe given it came from <paramref name="origin"/>.</summary>
    public double OverripeProbabilityGiven(FarmOrigin origin) =>
        _overripeGivenOrigin.GetValueOrDefault(origin);

    /// <summary>Draw a farm of origin from the configured weights.</summary>
    public FarmOrigin SampleOrigin(IRandomSource random)
    {
        var roll = random.NextDouble();

        foreach (var (origin, cumulative) in _originCdf)
        {
            if (roll < cumulative)
            {
                return origin;
            }
        }

        // Floating point can leave `roll` just past the final cumulative bound.
        return _originCdf[^1].Origin;
    }

    private static Dictionary<TKey, double> Normalise<TKey>(
        Dictionary<TKey, double> weights,
        string name)
        where TKey : notnull
    {
        if (weights.Count == 0)
        {
            throw new InvalidOperationException($"{name} must contain at least one entry.");
        }

        if (weights.Values.Any(weight => weight < 0))
        {
            throw new InvalidOperationException($"{name} contains a negative weight.");
        }

        var total = weights.Values.Sum();

        if (total <= 0)
        {
            throw new InvalidOperationException($"{name} weights sum to zero.");
        }

        return weights.ToDictionary(pair => pair.Key, pair => pair.Value / total);
    }
}
