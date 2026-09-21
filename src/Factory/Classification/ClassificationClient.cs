using System.Net.Http.Json;
using System.Text.Json.Serialization;
using BananaFarm.Contracts;

namespace BananaFarm.Factory.Classification;

/// <summary>HTTP client for the classification service's batch endpoint.</summary>
public sealed class ClassificationClient : IClassificationClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ClassificationClient> _logger;

    public ClassificationClient(HttpClient http, ILogger<ClassificationClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BananaClassification>> ClassifyAsync(
        IReadOnlyList<Banana> bananas,
        CancellationToken cancellationToken)
    {
        if (bananas.Count == 0)
        {
            return [];
        }

        using var response = await _http.PostAsJsonAsync(
                "/classify/batch",
                new BatchRequest(bananas),
                BananaJson.Options,
                cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            throw new HttpRequestException(
                $"Classification service returned {(int)response.StatusCode} for a batch of " +
                $"{bananas.Count}: {body}");
        }

        var payload = await response.Content
            .ReadFromJsonAsync<BatchResponse>(BananaJson.Options, cancellationToken)
            .ConfigureAwait(false);

        if (payload is null || payload.Results.Count != bananas.Count)
        {
            throw new HttpRequestException(
                $"Classification service returned {payload?.Results.Count ?? 0} results for a " +
                $"batch of {bananas.Count}. The batch endpoint must answer every banana.");
        }

        _logger.LogDebug("Classified a batch of {Count}.", bananas.Count);

        return [.. payload.Results.Select(result => result.Classification)];
    }

    private sealed record BatchRequest(
        [property: JsonPropertyName("bananas")] IReadOnlyList<Banana> Bananas);

    private sealed record BatchResponse(
        [property: JsonPropertyName("results")] IReadOnlyList<BatchResult> Results);

    private sealed record BatchResult(
        [property: JsonPropertyName("bananaId")] Guid BananaId,
        [property: JsonPropertyName("classification")] BananaClassification Classification);
}
