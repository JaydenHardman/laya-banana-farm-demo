using BananaFarm.Farm;
using BananaFarm.Farm.Generation;
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
builder.Services.AddBananaMessaging(builder.Configuration);
builder.Services.AddHostedService<HarvestWorker>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "farm" }));

app.MapGet("/rate", (WaveRateController waves, IOptions<FarmOptions> options) => Results.Ok(new
{
    regime = waves.Regime.ToString(),
    multiplier = waves.Multiplier,
    baseRatePerSecond = options.Value.BaseRatePerSecond,
    currentRatePerSecond = options.Value.BaseRatePerSecond * waves.Multiplier,
}));

app.Run();
