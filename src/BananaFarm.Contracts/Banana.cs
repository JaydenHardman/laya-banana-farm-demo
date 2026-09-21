namespace BananaFarm.Contracts;

/// <summary>
/// A single harvested banana, as produced by the farm service.
/// </summary>
/// <param name="Id">Unique identity of this banana.</param>
/// <param name="DateHarvested">When the banana was harvested (UTC).</param>
/// <param name="FarmOrigin">Which farm produced it.</param>
/// <param name="Weight">Weight in grams.</param>
/// <param name="Ripeness">0 = raw, 0.5 = perfect, 1 = expired.</param>
/// <param name="Grade">Grade assigned at harvest.</param>
/// <param name="Price">
/// Price in currency units. Deliberately <see cref="decimal"/> rather than a float: prices
/// are summed per box and averaged across the market, where binary floating point would
/// accumulate error.
/// </param>
public sealed record Banana(
    Guid Id,
    DateTimeOffset DateHarvested,
    FarmOrigin FarmOrigin,
    float Weight,
    float Ripeness,
    BananaGrade Grade,
    decimal Price)
{
    /// <summary>
    /// Human-readable rendering handed to the classification model as its input state.
    /// </summary>
    public string ToModelState() =>
        $"Banana harvested {DateHarvested:yyyy-MM-dd} from farm {FarmOrigin}. " +
        $"Weight {Weight:F1}g. Ripeness {Ripeness:F3} on a scale where 0 is raw, " +
        $"0.5 is perfectly ripe and 1 is expired. Harvest grade {Grade}. Price {Price:F2}.";
}
