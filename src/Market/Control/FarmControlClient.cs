using System.Net.Http.Json;
using System.Text.Json;
using BananaFarm.Contracts;

namespace BananaFarm.Market.Control;

/// <summary>A requested change to the farm's production. Both fields are optional.</summary>
public sealed record ProductionChangeRequest(bool? Running, double? BaseRatePerSecond);

/// <summary>Outcome of a call to the farm, preserving its status code and body.</summary>
public sealed record FarmCallResult(bool Ok, int StatusCode, JsonElement? Body, string? Error);

/// <summary>
/// Talks to the farm's production control endpoints on the dashboard's behalf.
/// </summary>
/// <remarks>
/// The dashboard is served by this service, so routing its control calls through here keeps
/// the browser on a single origin: no CORS configuration on the farm, and no farm port
/// baked into the page. The payloads are passed through untouched, so the farm remains the
/// only place that decides what a valid change is.
/// </remarks>
public sealed class FarmControlClient
{
    private const string ProductionPath = "/production";

    private readonly HttpClient _http;
    private readonly ILogger<FarmControlClient> _logger;

    public FarmControlClient(HttpClient http, ILogger<FarmControlClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>Read the farm's current production state.</summary>
    public Task<FarmCallResult> GetProductionAsync(CancellationToken cancellationToken) =>
        SendAsync(() => _http.GetAsync(ProductionPath, cancellationToken), cancellationToken);

    /// <summary>Ask the farm to start, stop, or change rate.</summary>
    public Task<FarmCallResult> SetProductionAsync(
        ProductionChangeRequest request,
        CancellationToken cancellationToken) =>
        SendAsync(
            () => _http.PostAsJsonAsync(ProductionPath, request, BananaJson.Options, cancellationToken),
            cancellationToken);

    private async Task<FarmCallResult> SendAsync(
        Func<Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await send().ConfigureAwait(false);

            var body = await response.Content
                .ReadFromJsonAsync<JsonElement>(cancellationToken)
                .ConfigureAwait(false);

            return new FarmCallResult(
                Ok: response.IsSuccessStatusCode,
                StatusCode: (int)response.StatusCode,
                Body: body,
                Error: null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // The farm being down is an ordinary state for a dashboard to display, not a
            // failure of this service.
            _logger.LogWarning(ex, "Could not reach the farm's production endpoint.");

            return new FarmCallResult(
                Ok: false,
                StatusCode: 503,
                Body: null,
                Error: "The farm service is not reachable.");
        }
    }
}
