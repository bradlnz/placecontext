using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace PlaceContext.App.Proxy;

/// <summary>Authenticated HTTP seam used only by App-owned API compositions.</summary>
public sealed class EdgeHttpClient(
    IHttpClientFactory httpClientFactory,
    IOptions<MicroserviceProxyOptions> options,
    IConfiguration configuration)
{
    public async Task<JsonElement> GetAsync(
        string service,
        string path,
        string callerToken,
        CancellationToken cancellationToken,
        bool useApiKey = false)
        => await SendAsync(service, HttpMethod.Get, path, callerToken, null, cancellationToken, useApiKey);

    public async Task<JsonElement> PostAsync(
        string service,
        string path,
        string callerToken,
        object body,
        CancellationToken cancellationToken,
        bool useApiKey = false)
        => await SendAsync(service, HttpMethod.Post, path, callerToken, body, cancellationToken, useApiKey);

    private async Task<JsonElement> SendAsync(
        string service,
        HttpMethod method,
        string path,
        string callerToken,
        object? body,
        CancellationToken cancellationToken,
        bool useApiKey)
    {
        if (!options.Value.Destinations.TryGetValue(service, out var configured)
            || !Uri.TryCreate(configured, UriKind.Absolute, out var origin))
            throw new EdgeHttpException(StatusCodes.Status503ServiceUnavailable, $"The {service} service is unavailable.");

        var root = origin.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? origin
            : new Uri(origin.AbsoluteUri + '/', UriKind.Absolute);
        using var request = new HttpRequestMessage(method, new Uri(root, path.TrimStart('/')));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            useApiKey
                ? configuration["PlaceContext:Api:Key"]
                    ?? throw new EdgeHttpException(StatusCodes.Status503ServiceUnavailable, "The service API key is unavailable.")
                : callerToken);
        if (body is not null) request.Content = JsonContent.Create(body);

        try
        {
            using var response = await httpClientFactory
                .CreateClient(MicroserviceProxyMiddleware.HttpClientName)
                .SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var message = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new EdgeHttpException((int)response.StatusCode, message);
            }

            if (response.Content.Headers.ContentLength == 0) return JsonSerializer.SerializeToElement(new { });
            return (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken)).Clone();
        }
        catch (HttpRequestException exception)
        {
            throw new EdgeHttpException(StatusCodes.Status502BadGateway, exception.Message);
        }
    }
}
