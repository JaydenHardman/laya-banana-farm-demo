namespace BananaFarm.Contracts;

/// <summary>RabbitMQ exchange and routing key names shared across all services.</summary>
public static class Topics
{
    /// <summary>The single topic exchange every service publishes to and consumes from.</summary>
    public const string Exchange = "banana";

    /// <summary>Raw bananas emitted by the farm.</summary>
    public const string BananaProduction = "bananaProduction";

    /// <summary>A golden banana, published on its own and never boxed.</summary>
    public const string GoldenBanana = "goldenBanana";

    /// <summary>A banana the classifier said should be thrown away.</summary>
    public const string PastRipe = "pastRipe";

    /// <summary>Bananas from a box that failed to fill within the timeout.</summary>
    public const string TooLongToFill = "tooLongToFill";

    /// <summary>A box that reached capacity.</summary>
    public const string FilledBox = "filledBox";

    /// <summary>Every routing key this system publishes.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        BananaProduction,
        GoldenBanana,
        PastRipe,
        TooLongToFill,
        FilledBox,
    ];
}
