using BananaFarm.Factory;
using BananaFarm.Factory.Boxing;
using BananaFarm.Factory.Classification;
using BananaFarm.Factory.Processing;
using BananaFarm.Messaging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<FactoryOptions>()
    .Bind(builder.Configuration.GetSection(FactoryOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<RedisOptions>()
    .Bind(builder.Configuration.GetSection(RedisOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton<IConnectionMultiplexer>(provider =>
{
    var options = provider.GetRequiredService<IOptions<RedisOptions>>().Value;
    var configuration = ConfigurationOptions.Parse(options.Configuration);

    // Compose starts everything at once. Connecting lazily rather than aborting lets the
    // service come up before Redis does and reconnect on its own.
    configuration.AbortOnConnectFail = false;
    configuration.ConnectRetry = 5;

    return ConnectionMultiplexer.Connect(configuration);
});

var classificationBaseAddress =
    builder.Configuration["Classification:BaseAddress"] ?? "http://classification:8000";

builder.Services.AddHttpClient<IClassificationClient, ClassificationClient>(client =>
{
    client.BaseAddress = new Uri(classificationBaseAddress);

    // Generous: a cold CPU inference can take hundreds of milliseconds per banana, and a
    // full batch multiplies that.
    client.Timeout = TimeSpan.FromSeconds(120);
});

builder.Services.AddSingleton<IClassificationCache, RedisClassificationCache>();
builder.Services.AddSingleton<ClassificationPipeline>();
builder.Services.AddSingleton<IBananaClassifier>(provider =>
    provider.GetRequiredService<ClassificationPipeline>());
builder.Services.AddHostedService(provider =>
    provider.GetRequiredService<ClassificationPipeline>());

builder.Services.AddSingleton<IBoxRepository>(provider =>
{
    var options = provider.GetRequiredService<IOptions<FactoryOptions>>().Value;

    return options.BoxStore.Equals("memory", StringComparison.OrdinalIgnoreCase)
        ? ActivatorUtilities.CreateInstance<InMemoryBoxRepository>(provider)
        : ActivatorUtilities.CreateInstance<RedisBoxRepository>(provider);
});

builder.Services.AddSingleton<BananaProcessor>();
builder.Services.AddBananaMessaging(builder.Configuration);
builder.Services.AddHostedService<ProductionConsumer>();
builder.Services.AddHostedService<BoxTimeoutSweeper>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "factory" }));

app.MapGet("/stats", async (
    ClassificationPipeline pipeline,
    IBoxRepository boxes,
    CancellationToken cancellationToken) =>
{
    var hits = pipeline.CacheHits;
    var misses = pipeline.CacheMisses;
    var total = hits + misses;

    return Results.Ok(new
    {
        classifiedTotal = total,
        cacheHits = hits,
        cacheMisses = misses,
        cacheHitRate = total == 0 ? 0d : (double)hits / total,
        openBoxes = await boxes.CountOpenAsync(cancellationToken),
    });
});

app.Run();
