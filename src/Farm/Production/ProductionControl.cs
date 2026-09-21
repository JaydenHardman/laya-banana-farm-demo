using BananaFarm.Farm.Generation;
using Microsoft.Extensions.Options;

namespace BananaFarm.Farm.Production;

/// <summary>The farm's current production settings.</summary>
/// <param name="Running">Whether bananas are being generated at all.</param>
/// <param name="BaseRatePerSecond">Mean bananas per second before wave multipliers.</param>
public sealed record ProductionState(bool Running, double BaseRatePerSecond);

/// <summary>Result of a requested change, so the caller learns why a value was refused.</summary>
public sealed record ProductionChangeResult(bool Accepted, ProductionState State, string? Error);

/// <summary>
/// Runtime control over generation: whether the farm is producing, and how fast.
/// </summary>
/// <remarks>
/// The rate lives here rather than in <see cref="FarmOptions"/> because it changes while the
/// service runs. <see cref="FarmOptions.BaseRatePerSecond"/> remains the value the farm
/// starts at; this holds the value currently in force. Reads happen on every generation tick
/// and writes come from the control API, so access is guarded.
/// </remarks>
public sealed class ProductionControl
{
    /// <summary>Slowest rate that can be requested. Use stop rather than a rate of zero.</summary>
    public const double MinRatePerSecond = 0.1;

    /// <summary>Fastest rate that can be requested.</summary>
    public const double MaxRatePerSecond = 100_000;

    private readonly Lock _gate = new();
    private readonly ILogger<ProductionControl> _logger;

    private bool _running;
    private double _ratePerSecond;

    public ProductionControl(IOptions<FarmOptions> options, ILogger<ProductionControl> logger)
    {
        _logger = logger;
        _ratePerSecond = options.Value.BaseRatePerSecond;
        _running = true;
    }

    /// <summary>The settings currently in force.</summary>
    public ProductionState Current
    {
        get
        {
            lock (_gate)
            {
                return new ProductionState(_running, _ratePerSecond);
            }
        }
    }

    /// <summary>
    /// Apply a change. Both arguments are optional, so a caller can start or stop without
    /// restating the rate, and change the rate without restating whether it is running.
    /// </summary>
    public ProductionChangeResult Apply(bool? running, double? ratePerSecond)
    {
        if (ratePerSecond is { } requested &&
            (double.IsNaN(requested) || requested < MinRatePerSecond || requested > MaxRatePerSecond))
        {
            return new ProductionChangeResult(
                Accepted: false,
                State: Current,
                Error: $"Rate must be between {MinRatePerSecond} and {MaxRatePerSecond} bananas " +
                       "per second. To halt production, stop it rather than requesting a rate " +
                       "of zero.");
        }

        ProductionState state;
        bool runningChanged;
        bool rateChanged;

        lock (_gate)
        {
            runningChanged = running is { } wantedRunning && wantedRunning != _running;
            rateChanged = ratePerSecond is { } wantedRate
                && Math.Abs(wantedRate - _ratePerSecond) > 1e-9;

            if (running is { } newRunning)
            {
                _running = newRunning;
            }

            if (ratePerSecond is { } newRate)
            {
                _ratePerSecond = newRate;
            }

            state = new ProductionState(_running, _ratePerSecond);
        }

        if (runningChanged)
        {
            _logger.LogInformation(
                "Production {Action} by request.",
                state.Running ? "started" : "stopped");
        }

        if (rateChanged)
        {
            _logger.LogInformation(
                "Base rate set to {Rate}/s by request.",
                state.BaseRatePerSecond);
        }

        return new ProductionChangeResult(Accepted: true, State: state, Error: null);
    }
}
