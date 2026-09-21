using System.Collections.Concurrent;
using System.Threading.Channels;
using BananaFarm.Contracts;
using Microsoft.Extensions.Options;

namespace BananaFarm.Factory.Classification;

/// <summary>
/// The factory's route to a classification: cache, then in-flight de-duplication, then a
/// batched call to the model.
/// </summary>
/// <remarks>
/// <para>Three layers sit between 200 bananas/second and a model that answers 2-3 per second:</para>
/// <list type="number">
/// <item>the bucket cache, which absorbs the overwhelming majority of traffic;</item>
/// <item>in-flight de-duplication, so a burst of identical buckets makes one model call
/// rather than one per banana;</item>
/// <item>batching, so the remaining misses amortise HTTP overhead.</item>
/// </list>
/// </remarks>
public sealed class ClassificationPipeline : BackgroundService, IBananaClassifier
{
    private readonly IClassificationCache _cache;
    private readonly IClassificationClient _client;
    private readonly FactoryOptions _options;
    private readonly ILogger<ClassificationPipeline> _logger;

    private readonly Channel<PendingClassification> _queue =
        Channel.CreateUnbounded<PendingClassification>(new UnboundedChannelOptions
        {
            SingleReader = true,
        });

    private readonly ConcurrentDictionary<string, Task<BananaClassification>> _inFlight = new();
    private readonly SemaphoreSlim _dispatchSlots;

    private long _hits;
    private long _misses;

    public ClassificationPipeline(
        IClassificationCache cache,
        IClassificationClient client,
        IOptions<FactoryOptions> options,
        ILogger<ClassificationPipeline> logger)
    {
        _cache = cache;
        _client = client;
        _options = options.Value;
        _logger = logger;
        _dispatchSlots = new SemaphoreSlim(_options.ClassifierConcurrency);
    }

    /// <summary>Cache hits since startup.</summary>
    public long CacheHits => Interlocked.Read(ref _hits);

    /// <summary>Cache misses since startup.</summary>
    public long CacheMisses => Interlocked.Read(ref _misses);

    public async Task<BananaClassification> ClassifyAsync(
        Banana banana,
        CancellationToken cancellationToken)
    {
        var bucket = ClassificationBucket.KeyFor(banana, _options);

        var cached = await _cache.GetAsync(bucket, cancellationToken).ConfigureAwait(false);

        if (cached is not null)
        {
            Interlocked.Increment(ref _hits);
            return cached;
        }

        Interlocked.Increment(ref _misses);

        // GetOrAdd can invoke the factory more than once under contention, so the pending
        // entry is only enqueued by the caller that actually won the slot.
        var pending = new PendingClassification(bucket, banana);
        var task = _inFlight.GetOrAdd(bucket, _ => pending.Completion.Task);

        if (ReferenceEquals(task, pending.Completion.Task))
        {
            await _queue.Writer.WriteAsync(pending, cancellationToken).ConfigureAwait(false);
        }

        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batchDelay = TimeSpan.FromMilliseconds(_options.ClassifierBatchDelayMilliseconds);
        var reader = _queue.Reader;

        while (await reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
        {
            var batch = await CollectBatchAsync(reader, batchDelay, stoppingToken)
                .ConfigureAwait(false);

            if (batch.Count == 0)
            {
                continue;
            }

            await _dispatchSlots.WaitAsync(stoppingToken).ConfigureAwait(false);

            // Fire and forget: the batch completes its own pending tasks, and the loop must
            // stay free to collect the next one.
            _ = DispatchAsync(batch, stoppingToken);
        }
    }

    private async Task<List<PendingClassification>> CollectBatchAsync(
        ChannelReader<PendingClassification> reader,
        TimeSpan batchDelay,
        CancellationToken stoppingToken)
    {
        var batch = new List<PendingClassification>(_options.ClassifierBatchSize);

        while (batch.Count < _options.ClassifierBatchSize && reader.TryRead(out var pending))
        {
            batch.Add(pending);
        }

        if (batch.Count >= _options.ClassifierBatchSize)
        {
            return batch;
        }

        // Give stragglers a brief window to join rather than sending a batch of one.
        using var window = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        window.CancelAfter(batchDelay);

        try
        {
            while (batch.Count < _options.ClassifierBatchSize)
            {
                var pending = await reader.ReadAsync(window.Token).ConfigureAwait(false);
                batch.Add(pending);
            }
        }
        catch (OperationCanceledException)
        {
            // Window elapsed, or shutdown. Either way, send what has accumulated.
        }

        return batch;
    }

    private async Task DispatchAsync(
        List<PendingClassification> batch,
        CancellationToken cancellationToken)
    {
        try
        {
            var bananas = batch.ConvertAll(pending => pending.Banana);
            var results = await _client.ClassifyAsync(bananas, cancellationToken)
                .ConfigureAwait(false);

            for (var i = 0; i < batch.Count; i++)
            {
                var pending = batch[i];
                var classification = results[i];

                await _cache.SetAsync(pending.BucketKey, classification, cancellationToken)
                    .ConfigureAwait(false);

                pending.Completion.TrySetResult(classification);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Classification batch of {Count} failed.", batch.Count);

            foreach (var pending in batch)
            {
                pending.Completion.TrySetException(ex);
            }
        }
        finally
        {
            foreach (var pending in batch)
            {
                _inFlight.TryRemove(pending.BucketKey, out _);
            }

            _dispatchSlots.Release();
        }
    }

    public override void Dispose()
    {
        _dispatchSlots.Dispose();
        base.Dispose();
    }

    private sealed record PendingClassification(string BucketKey, Banana Banana)
    {
        public TaskCompletionSource<BananaClassification> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
