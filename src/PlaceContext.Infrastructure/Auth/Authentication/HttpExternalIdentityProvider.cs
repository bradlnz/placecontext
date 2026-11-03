using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.Text.Json;
using PlaceContext.Application.Auth;

namespace PlaceContext.Infrastructure.Auth;

public sealed class HttpExternalIdentityProvider(IHttpClientFactory _httpClients) : IExternalIdentityProvider
{
    public async Task<SsoEndpoints> ResolveEndpointsAsync(SsoOptions options, CancellationToken ct)
    {
        if (!options.OpenIdConnect)
        {
            return new SsoEndpoints(
                new Uri(options.BaseUri, "/oauth/authorize").ToString(),
                new Uri(options.BaseUri, "/oauth/token").ToString(),
                new Uri(options.BaseUri, "/oauth/userinfo").ToString(),
                "identity");
        }

        var discoveryUrl = $"{options.BaseUri.ToString().TrimEnd('/')}/.well-known/openid-configuration";
        using var response = await _httpClients.CreateClient().GetAsync(discoveryUrl, ct);
        response.EnsureSuccessStatusCode();
        var metadata = JsonSerializer.Deserialize<OpenIdMetadata>(
            await response.Content.ReadAsStringAsync(ct));
        if (metadata is null
            || !IsHttpsUrl(metadata.AuthorizationEndpoint)
            || !IsHttpsUrl(metadata.TokenEndpoint)
            || !IsHttpsUrl(metadata.UserInfoEndpoint))
        {
            throw new JsonException("OIDC discovery metadata is incomplete.");
        }
        return new SsoEndpoints(
            metadata.AuthorizationEndpoint,
            metadata.TokenEndpoint,
            metadata.UserInfoEndpoint,
            "openid email profile");
    }

    private static bool IsHttpsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    public async Task<ExternalIdentity> ExchangeAsync(SsoOptions options, string code, string verifier, CancellationToken ct)
    {
        using var client = _httpClients.CreateClient();
        var endpoints = await ResolveEndpointsAsync(options, ct);
        using var tokenResponse = await client.PostAsync(endpoints.TokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["client_id"] = options.ClientId,
                ["redirect_uri"] = options.CallbackUrl,
                ["code_verifier"] = verifier,
            }), ct);
        tokenResponse.EnsureSuccessStatusCode();
        var token = JsonSerializer.Deserialize<TokenResponse>(
            await tokenResponse.Content.ReadAsStringAsync(ct));
        if (string.IsNullOrWhiteSpace(token?.AccessToken))
        {
            throw new JsonException("Missing access token.");
        }
        using var identityRequest = new HttpRequestMessage(HttpMethod.Get, endpoints.UserInfoEndpoint);
        identityRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var identityResponse = await client.SendAsync(identityRequest, ct);
        identityResponse.EnsureSuccessStatusCode();
        var identity = JsonSerializer.Deserialize<SsoIdentity>(
            await identityResponse.Content.ReadAsStringAsync(ct));
        if (identity is null || string.IsNullOrWhiteSpace(identity.Subject)
            || string.IsNullOrWhiteSpace(identity.Email))
        {
            throw new JsonException("The identity response is incomplete.");
        }
        return new ExternalIdentity(identity.Subject, identity.Email, identity.Name, identity.Role);
    }
    private sealed record OpenIdMetadata(
        [property: JsonPropertyName("authorization_endpoint")] string AuthorizationEndpoint,
        [property: JsonPropertyName("token_endpoint")] string TokenEndpoint,
        [property: JsonPropertyName("userinfo_endpoint")] string UserInfoEndpoint);
    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);
    private sealed record SsoIdentity(
        [property: JsonPropertyName("sub")] string Subject,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("role")] string? Role);
}
