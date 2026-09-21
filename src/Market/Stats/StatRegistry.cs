namespace BananaFarm.Market.Stats;

/// <summary>
/// The set of stats this service exposes, discovered from DI at startup.
/// </summary>
public sealed class StatRegistry
{
    private readonly Dictionary<string, IStat> _stats;

    public StatRegistry(IEnumerable<IStat> stats)
    {
        _stats = new Dictionary<string, IStat>(StringComparer.OrdinalIgnoreCase);

        foreach (var stat in stats)
        {
            if (!_stats.TryAdd(stat.Id, stat))
            {
                throw new InvalidOperationException(
                    $"Two stats are registered with the id '{stat.Id}'. Stat ids appear in URLs " +
                    "and subscriptions and must be unique.");
            }
        }
    }

    /// <summary>Every registered stat, ordered by id.</summary>
    public IReadOnlyList<IStat> All => [.. _stats.Values.OrderBy(stat => stat.Id, StringComparer.Ordinal)];

    /// <summary>Every registered stat id.</summary>
    public IReadOnlyList<string> Ids => [.. All.Select(stat => stat.Id)];

    /// <summary>Look up a stat by id.</summary>
    public bool TryGet(string id, out IStat stat) => _stats.TryGetValue(id, out stat!);

    /// <summary>The catalogue entry for every stat.</summary>
    public IReadOnlyList<StatDescriptor> Describe() =>
        [.. All.Select(stat => new StatDescriptor(stat.Id, stat.Name, stat.Unit, stat.Description))];
}
