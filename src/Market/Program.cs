using System.Net.WebSockets;
using BananaFarm.Market;
using BananaFarm.Market.Ingest;
using BananaFarm.Market.Live;
using BananaFarm.Market.Metrics;
using BananaFarm.Market.Stats;
using BananaFarm.Messaging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<MarketOptions>()
    .Bind(builder.Configuration.GetSection(MarketOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton<IConnectionMultiplexer>(provider =>
{
    var options = provider.GetRequiredService<IOptions<MarketOptions>>().Value;
    var configuration = ConfigurationOptions.Parse(options.RedisConfiguration);
    configuration.AbortOnConnectFail = false;
    configuration.ConnectRetry = 5;

    return ConnectionMultiplexer.Connect(configuration);
});

builder.Services.AddSingleton<IMarketMetricsStore, RedisMarketMetricsStore>();

// Every IStat registered here is picked up by the catalogue, the per-stat routes and the
// WebSocket automatically. Adding a metric is one class and one line.
builder.Services.AddSingleton<IStat, AverageBoxPriceStat>();
builder.Services.AddSingleton<IStat, PastDueLossStat>();
builder.Services.AddSingleton<IStat, GoldenCountStat>();
builder.Services.AddSingleton<StatRegistry>();
builder.Services.AddSingleton<StatsLiveHandler>();
builder.Services.AddSingleton<MarketRecorder>();

builder.Services.AddBananaMessaging(builder.Configuration);
builder.Services.AddHostedService<MarketConsumer>();

var app = builder.Build();

app.UseWebSockets();

// The dashboard is served by this service rather than a container of its own, so it shares
// an origin with the API and the WebSocket: no CORS, no second image, still one compose up.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "market" }));

// Catalogue only. Values are deliberately absent so this response does not grow with the
// number of stats, and adding a metric never reshapes an existing payload.
app.MapGet("/stats", (StatRegistry registry) => Results.Ok(new
{
    stats = registry.Describe().Select(stat => new
    {
        stat.Id,
        stat.Name,
        stat.Unit,
        stat.Description,
        href = $"/stats/{stat.Id}",
        byFarmHref = $"/stats/{stat.Id}/by-farm",
    }),
    live = "/stats/live",
}));

app.MapGet("/stats/{statId}", async (
    string statId,
    StatRegistry registry,
    IMarketMetricsStore store,
    CancellationToken cancellationToken) =>
{
    if (!registry.TryGet(statId, out var stat))
    {
        return UnknownStat(statId, registry);
    }

    return Results.Ok(await stat.ComputeAsync(store, cancellationToken));
});

app.MapGet("/stats/{statId}/by-farm", async (
    string statId,
    StatRegistry registry,
    IMarketMetricsStore store,
    CancellationToken cancellationToken) =>
{
    if (!registry.TryGet(statId, out var stat))
    {
        return UnknownStat(statId, registry);
    }

    var byFarm = await stat.ComputeByFarmAsync(store, cancellationToken);

    return Results.Ok(new
    {
        id = stat.Id,
        unit = stat.Unit,
        asOf = DateTimeOffset.UtcNow,
        farms = byFarm.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
    });
});

app.Map("/stats/live", async (HttpContext context, StatsLiveHandler handler) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        return Results.BadRequest(new
        {
            error = "This endpoint requires a WebSocket upgrade.",
            example = "{\"subscribe\": [\"average-box-price\"], \"intervalSeconds\": 5}",
        });
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();

    try
    {
        await handler.HandleAsync(socket, context.RequestAborted);
    }
    catch (OperationCanceledException)
    {
        // Client disconnected or the host is shutting down. Neither is an error.
    }
    catch (WebSocketException)
    {
        // Connection dropped mid-write.
    }

    return Results.Empty;
});

app.Run();

static IResult UnknownStat(string statId, StatRegistry registry) => Results.NotFound(new
{
    error = $"No stat with id '{statId}'.",
    validStats = registry.Ids,
});
