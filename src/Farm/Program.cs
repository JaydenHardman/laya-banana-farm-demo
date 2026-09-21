using BananaFarm.Farm;
using BananaFarm.Farm.Generation;
using BananaFarm.Farm.Production;
using BananaFarm.Farm.Waves;
using BananaFarm.Messaging;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<FarmOptions>()
    .Bind(builder.Configuration.GetSection(FarmOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<WaveOptions>()
    .Bind(builder.Configuration.GetSection(WaveOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IRandomSource, SystemRandomSource>();

// Validated eagerly at startup: an unsatisfiable distribution config should fail the
// container, not silently produce the wrong population.
builder.Services.AddSingleton(provider =>
    new OriginRipenessModel(
        provider.GetRequiredService<IOptions<FarmOptions>>().Value));

builder.Services.AddSingleton<BananaGenerator>();
builder.Services.AddSingleton<WaveRateController>();
builder.Services.AddSingleton<ProductionControl>();
builder.Services.AddBananaMessaging(builder.Configuration);
builder.Services.AddHostedService<HarvestWorker>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "farm" }));

// Production control. GET reports what the farm is doing; POST changes it. Both fields on
// the POST body are optional, so start/stop and rate changes are independent.
app.MapGet("/production", (ProductionControl production, WaveRateController waves) =>
    Results.Ok(Describe(production.Current, waves)));

app.MapPost("/production", (
    ProductionChangeRequest request,
    ProductionControl production,
    WaveRateController waves) =>
{
    var result = production.Apply(request.Running, request.BaseRatePerSecond);

    return result.Accepted
        ? Results.Ok(Describe(result.State, waves))
        : Results.BadRequest(new { error = result.Error, current = Describe(result.State, waves) });
});

app.Run();

static object Describe(ProductionState state, WaveRateController waves) => new
{
    running = state.Running,
    baseRatePerSecond = state.BaseRatePerSecond,
    regime = waves.Regime.ToString(),
    multiplier = waves.Multiplier,
    // Zero while stopped: the wave multiplier is stale and would otherwise imply output.
    currentRatePerSecond = state.Running ? state.BaseRatePerSecond * waves.Multiplier : 0,
    minRatePerSecond = ProductionControl.MinRatePerSecond,
    maxRatePerSecond = ProductionControl.MaxRatePerSecond,
};

/// <summary>Body of a production change request; both fields are optional.</summary>
internal sealed record ProductionChangeRequest(bool? Running, double? BaseRatePerSecond);
