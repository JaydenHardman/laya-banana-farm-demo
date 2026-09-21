using System.ComponentModel.DataAnnotations;

namespace BananaFarm.Farm.Waves;

/// <summary>Shape of the production waves layered on top of the farm's base rate.</summary>
public sealed class WaveOptions
{
    public const string SectionName = "Waves";

    /// <summary>Rate multiplier targeted while in a downturn.</summary>
    [Range(0.01, 100)]
    public double DownturnMultiplier { get; set; } = 0.25;

    /// <summary>Rate multiplier targeted during normal production.</summary>
    [Range(0.01, 100)]
    public double NormalMultiplier { get; set; } = 1.0;

    /// <summary>Rate multiplier targeted during a spike.</summary>
    [Range(0.01, 100)]
    public double SpikeMultiplier { get; set; } = 2.6;

    /// <summary>Average seconds spent in a downturn before switching regime.</summary>
    [Range(0.1, 3600)]
    public double DownturnDwellSeconds { get; set; } = 8;

    /// <summary>Average seconds spent in normal production before switching regime.</summary>
    [Range(0.1, 3600)]
    public double NormalDwellSeconds { get; set; } = 25;

    /// <summary>Average seconds spent in a spike before switching regime.</summary>
    [Range(0.1, 3600)]
    public double SpikeDwellSeconds { get; set; } = 4;

    /// <summary>
    /// How hard the multiplier is pulled toward the current regime's target. Higher values
    /// make waves change shape faster.
    /// </summary>
    [Range(0.01, 100)]
    public double ReversionRate { get; set; } = 1.2;

    /// <summary>Volatility of the multiplier's random walk. Higher values make it choppier.</summary>
    [Range(0, 100)]
    public double Volatility { get; set; } = 0.35;

    /// <summary>Lower clamp on the rate multiplier.</summary>
    [Range(0, 100)]
    public double MinMultiplier { get; set; } = 0.02;

    /// <summary>Upper clamp on the rate multiplier.</summary>
    [Range(0.01, 1000)]
    public double MaxMultiplier { get; set; } = 4.0;
}
