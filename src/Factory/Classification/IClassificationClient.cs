using BananaFarm.Contracts;

namespace BananaFarm.Factory.Classification;

/// <summary>Talks to the classification service.</summary>
public interface IClassificationClient
{
    /// <summary>
    /// Classify a batch, returning one result per banana in the order supplied.
    /// </summary>
    Task<IReadOnlyList<BananaClassification>> ClassifyAsync(
        IReadOnlyList<Banana> bananas,
        CancellationToken cancellationToken);
}

/// <summary>Reads and writes cached classifications.</summary>
public interface IClassificationCache
{
    /// <summary>Look up a bucket, returning null on a miss.</summary>
    Task<BananaClassification?> GetAsync(string bucketKey, CancellationToken cancellationToken);

    /// <summary>Store a classification against its bucket.</summary>
    Task SetAsync(
        string bucketKey,
        BananaClassification classification,
        CancellationToken cancellationToken);
}

/// <summary>Classifies a banana, from cache where possible.</summary>
public interface IBananaClassifier
{
    /// <summary>Classify one banana.</summary>
    Task<BananaClassification> ClassifyAsync(Banana banana, CancellationToken cancellationToken);
}
