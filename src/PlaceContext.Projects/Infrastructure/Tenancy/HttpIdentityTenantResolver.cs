using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Application.Ports;

namespace PlaceContext.Projects.Infrastructure.Tenancy;

public sealed class HttpIdentityTenantResolver(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : IRequestTenantResolver
{
    private const string ServiceAuthSection = "PlaceContext:ServiceAuth";
    private const string IdentitySection = "PlaceContext:Projects:Identity";

    public async Task<TenantContext?> ResolveAsync(string host, CancellationToken ct = default)
    {
        var origin = configuration[$"{IdentitySection}:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Identity"]
            ?? throw new InvalidOperationException(
                "Configure PlaceContext:Projects:Identity:BaseAddress for tenant resolution.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        var target = new Uri(
            new Uri(baseAddress),
            $"api/identity/internal/tenants/resolve?host={Uri.EscapeDataString(host)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, target);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateServiceToken());
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TenantContext>(ct);
    }

    private string CreateServiceToken()
    {
        var configuredToken = configuration[$"{IdentitySection}:ServiceToken"];
        if (!string.IsNullOrWhiteSpace(configuredToken))
            return configuredToken;

        var signingKey = configuration[$"{ServiceAuthSection}:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
        {
            throw new InvalidOperationException(
                "Projects requires an Identity service token or a ServiceAuth signing key.");
        }

        var now = DateTimeOffset.UtcNow;
        var header = EncodeJson(new Dictionary<string, object>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT",
        });
        var payload = EncodeJson(new Dictionary<string, object>
        {
            ["iss"] = configuration[$"{ServiceAuthSection}:Issuer"] ?? "placecontext",
            ["aud"] = configuration[$"{ServiceAuthSection}:Audience"] ?? "placecontext-services",
            ["sub"] = "projects",
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.AddSeconds(-5).ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(2).ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N"),
        });
        var unsignedToken = $"{header}.{payload}";
        var signature = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(signingKey),
            Encoding.ASCII.GetBytes(unsignedToken));
        return $"{unsignedToken}.{Base64Url(signature)}";
    }

    private static string EncodeJson(IReadOnlyDictionary<string, object> value)
        => Base64Url(JsonSerializer.SerializeToUtf8Bytes(value));

    private static string Base64Url(ReadOnlySpan<byte> value)
        => Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
