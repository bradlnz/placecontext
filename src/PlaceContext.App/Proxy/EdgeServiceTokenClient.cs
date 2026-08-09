using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace PlaceContext.App.Proxy;

public sealed class EdgeServiceTokenClient(
    IHttpClientFactory httpClientFactory,
    IOptions<MicroserviceProxyOptions> options,
    ILogger<EdgeServiceTokenClient> logger)
{
    public async Task<string?> ExchangeAsync(HttpContext context)
    {
        var cookie = context.Request.Headers.Cookie.ToString();
        if (string.IsNullOrWhiteSpace(cookie)) return null;
        if (!options.Value.Destinations.TryGetValue("Identity", out var configured)
            || !Uri.TryCreate(configured, UriKind.Absolute, out var identity))
            return null;

        var baseAddress = identity.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? identity
            : new Uri(identity.AbsoluteUri + "/", UriKind.Absolute);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(baseAddress, "api/identity/internal/service-token"));
        request.Headers.TryAddWithoutValidation(HeaderNames.Cookie, cookie);
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", context.Request.Host.Value);
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", context.Request.Scheme);

        try
        {
            using var response = await httpClientFactory
                .CreateClient(MicroserviceProxyMiddleware.HttpClientName)
                .SendAsync(request, context.RequestAborted);
            if (!response.IsSuccessStatusCode) return null;
            var payload = await response.Content.ReadFromJsonAsync<EdgeServiceTokenResponse>(
                context.RequestAborted);
            return payload?.AccessToken;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Identity service-token exchange failed.");
            return null;
        }
    }
}
