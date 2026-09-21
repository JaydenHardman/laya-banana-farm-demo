namespace BananaFarm.Farm.Generation;

/// <summary>
/// Source of randomness, injected so that every distribution in this service can be tested
/// deterministically.
/// </summary>
public interface IRandomSource
{
    /// <summary>Uniform value in <c>[0, 1)</c>.</summary>
    double NextDouble();

    /// <summary>Uniform value in <c>[min, max)</c>.</summary>
    double NextDouble(double min, double max);

    /// <summary>Sample from the standard normal distribution.</summary>
    double NextGaussian();
}

/// <summary>
/// <see cref="IRandomSource"/> over <see cref="Random"/>. Uses
/// <see cref="Random.Shared"/> by default, which is thread-safe.
/// </summary>
public sealed class SystemRandomSource : IRandomSource
{
    private readonly Random _random;

    public SystemRandomSource() : this(Random.Shared) { }

    public SystemRandomSource(Random random) => _random = random;

    public double NextDouble() => _random.NextDouble();

    public double NextDouble(double min, double max) => min + (_random.NextDouble() * (max - min));

    public double NextGaussian()
    {
        // Box-Muller. NextDouble() can return exactly 0, which Log would send to -infinity.
        double u1;
        do
        {
            u1 = _random.NextDouble();
        }
        while (u1 <= double.Epsilon);

        var u2 = _random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
