using BananaFarm.Contracts;
using Microsoft.Extensions.Options;

namespace BananaFarm.Farm.Generation;

/// <summary>Produces bananas satisfying every distribution invariant in SPEC 4.1.</summary>
public sealed class BananaGenerator
{
    private readonly FarmOptions _options;
    private readonly OriginRipenessModel _originModel;
    private readonly IRandomSource _random;
    private readonly (BananaGrade Grade, double Cumulative)[] _gradeCdf;
    private readonly TimeProvider _time;

    public BananaGenerator(
        IOptions<FarmOptions> options,
        OriginRipenessModel originModel,
        IRandomSource random,
        TimeProvider time)
    {
        _options = options.Value;
        _originModel = originModel;
        _random = random;
        _time = time;

        var total = _options.GradeWeights.Values.Sum();

        if (total <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(FarmOptions.GradeWeights)} must contain at least one positive weight.");
        }

        var cumulative = 0.0;
        _gradeCdf = [.. _options.GradeWeights.Select(pair =>
        {
            cumulative += pair.Value / total;
            return (pair.Key, cumulative);
        })];
    }

    /// <summary>Generate one banana.</summary>
    public Banana Next()
    {
        var origin = _originModel.SampleOrigin(_random);
        var ripeness = SampleRipeness(origin);
        var grade = SampleGrade();
        var weight = SampleWeight();
        var price = PriceCalculator.Calculate(grade, ripeness, _options.OverripeThreshold, _random);

        return new Banana(
            Id: Guid.NewGuid(),
            DateHarvested: _time.GetUtcNow(),
            FarmOrigin: origin,
            Weight: (float)weight,
            Ripeness: (float)ripeness,
            Grade: grade,
            Price: price);
    }

    /// <summary>Generate <paramref name="count"/> bananas.</summary>
    public IReadOnlyList<Banana> Next(int count)
    {
        if (count <= 0)
        {
            return [];
        }

        var bananas = new Banana[count];

        for (var i = 0; i < count; i++)
        {
            bananas[i] = Next();
        }

        return bananas;
    }

    private double SampleRipeness(FarmOrigin origin)
    {
        var threshold = _options.OverripeThreshold;

        if (_random.NextDouble() < _originModel.OverripeProbabilityGiven(origin))
        {
            return _random.NextDouble(threshold, 1.0);
        }

        return SampleTriangular(0, threshold, Math.Clamp(_options.IdealRipeness, 0, threshold));
    }

    /// <summary>
    /// Triangular distribution over <c>[min, max]</c> with its peak at <paramref name="mode"/>.
    /// Used for ordinary ripeness so most bananas cluster near ideal rather than spreading
    /// flat across the range.
    /// </summary>
    private double SampleTriangular(double min, double max, double mode)
    {
        var range = max - min;

        if (range <= 0)
        {
            return min;
        }

        var roll = _random.NextDouble();
        var modeFraction = (mode - min) / range;

        return roll < modeFraction
            ? min + Math.Sqrt(roll * range * (mode - min))
            : max - Math.Sqrt((1 - roll) * range * (max - mode));
    }

    private BananaGrade SampleGrade()
    {
        if (_random.NextDouble() < _options.GoldenProbability)
        {
            return BananaGrade.Golden;
        }

        var roll = _random.NextDouble();

        foreach (var (grade, cumulative) in _gradeCdf)
        {
            if (roll < cumulative)
            {
                return grade;
            }
        }

        return _gradeCdf[^1].Grade;
    }

    private double SampleWeight()
    {
        var weight = _options.WeightMeanGrams + (_random.NextGaussian() * _options.WeightStdDevGrams);
        return Math.Clamp(weight, _options.WeightMinGrams, _options.WeightMaxGrams);
    }
}
