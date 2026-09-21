using BananaFarm.Farm.Generation;
using Microsoft.Extensions.Options;

namespace BananaFarm.Farm.Waves;

/// <summary>Production regimes the farm cycles between.</summary>
public enum ProductionRegime
{
    Downturn,
    Normal,
    Spike,
}

/// <summary>
/// Drives the farm's instantaneous production rate so that output arrives in unpredictable
/// waves rather than at a constant tick.
/// </summary>
/// <remarks>
/// Two layers. A three-state Markov chain picks a <see cref="ProductionRegime"/>, switching
/// with probability <c>dt / meanDwell</c> each step, which gives exponentially distributed
/// regime durations. The regime sets the target of an Ornstein-Uhlenbeck random walk on a
/// rate multiplier, so the rate drifts toward the target with noise rather than stepping to
/// it. The result has sustained downturns, sharp spikes and a noisy middle, and never
/// repeats.
/// </remarks>
public sealed class WaveRateController
{
    private readonly WaveOptions _options;
    private readonly IRandomSource _random;
    private double _multiplier;

    public WaveRateController(IOptions<WaveOptions> options, IRandomSource random)
    {
        _options = options.Value;
        _random = random;
        _multiplier = _options.NormalMultiplier;
        Regime = ProductionRegime.Normal;
    }

    /// <summary>The regime currently in effect.</summary>
    public ProductionRegime Regime { get; private set; }

    /// <summary>The current rate multiplier applied to the farm's base rate.</summary>
    public double Multiplier => _multiplier;

    /// <summary>
    /// Advance the simulation by <paramref name="elapsed"/> and return the instantaneous
    /// production rate in bananas per second.
    /// </summary>
    public double Advance(TimeSpan elapsed, double baseRatePerSecond)
    {
        var dt = elapsed.TotalSeconds;

        if (dt <= 0)
        {
            return baseRatePerSecond * _multiplier;
        }

        AdvanceRegime(dt);
        AdvanceMultiplier(dt);

        return baseRatePerSecond * _multiplier;
    }

    private void AdvanceRegime(double dt)
    {
        var dwell = Regime switch
        {
            ProductionRegime.Downturn => _options.DownturnDwellSeconds,
            ProductionRegime.Spike => _options.SpikeDwellSeconds,
            _ => _options.NormalDwellSeconds,
        };

        if (_random.NextDouble() >= dt / dwell)
        {
            return;
        }

        // Spikes and downturns both settle back through Normal rather than flipping straight
        // into each other, which keeps the output from looking like a square wave.
        Regime = Regime == ProductionRegime.Normal
            ? (_random.NextDouble() < 0.5 ? ProductionRegime.Spike : ProductionRegime.Downturn)
            : ProductionRegime.Normal;
    }

    private void AdvanceMultiplier(double dt)
    {
        var target = Regime switch
        {
            ProductionRegime.Downturn => _options.DownturnMultiplier,
            ProductionRegime.Spike => _options.SpikeMultiplier,
            _ => _options.NormalMultiplier,
        };

        var drift = _options.ReversionRate * (target - _multiplier) * dt;
        var noise = _options.Volatility * Math.Sqrt(dt) * _random.NextGaussian();

        _multiplier = Math.Clamp(
            _multiplier + drift + noise,
            _options.MinMultiplier,
            _options.MaxMultiplier);
    }
}
