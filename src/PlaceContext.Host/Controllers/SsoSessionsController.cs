using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Controllers;

/// <summary>Provider-neutral OAuth 2.1/PKCE login backed by a configured identity authority.</summary>
[AllowAnonymous]
public sealed class SsoSessionsController : ControllerBase
{
    private const string StateCookie = "placecontext_external_sso";
    private readonly IAuthService _auth;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClients;
    private readonly IDataProtector _protector;

    public SsoSessionsController(
        IAuthService auth,
        IConfiguration configuration,
        IHttpClientFactory httpClients,
        IDataProtectionProvider dataProtection)
    {
        _auth = auth;
        _configuration = configuration;
        _httpClients = httpClients;
        _protector = dataProtection.CreateProtector("PlaceContext.ExternalSso.State.v1");
    }

    [HttpGet("/auth/sso")]
    public async Task<IActionResult> New(string? returnUrl)
    {
        var options = GetOptions();
        if (options is null) return StatusCode(503, "External sign-in is not configured.");

        SsoEndpoints endpoints;
        try
        {
            endpoints = await ResolveEndpointsAsync(options, HttpContext.RequestAborted);
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, "External sign-in authority could not be reached.");
        }
        catch (JsonException)
        {
            return StatusCode(503, "External sign-in authority returned invalid metadata.");
        }

        var verifier = RandomToken(64);
        var nonce = RandomToken(32);
        var state = new LoginState(nonce, verifier, LocalOrHome(returnUrl), DateTimeOffset.UtcNow);
        Response.Cookies.Append(StateCookie, _protector.Protect(JsonSerializer.Serialize(state)), new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10),
            Path = "/auth/sso",
        });

        var challenge = Base64UrlTextEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var authorizeUrl = QueryHelpers.AddQueryString(endpoints.AuthorizationEndpoint, new Dictionary<string, string?>
        {
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = options.CallbackUrl,
            ["response_type"] = "code",
            ["scope"] = endpoints.Scope,
            ["state"] = nonce,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        });
        return Redirect(authorizeUrl);
    }

    [HttpGet("/auth/sso/callback")]
    public async Task<IActionResult> Create(string? code, string? state)
    {
        var options = GetOptions();
        var loginState = ReadState();
        Response.Cookies.Delete(StateCookie, new CookieOptions { Path = "/auth/sso" });
        if (options is null || loginState is null || string.IsNullOrWhiteSpace(code)
            || !SecureEquals(loginState.Nonce, state) || loginState.IssuedAt < DateTimeOffset.UtcNow.AddMinutes(-10))
            return LoginError("External sign-in expired. Please try again.");

        try
        {
            var client = _httpClients.CreateClient();
            var endpoints = await ResolveEndpointsAsync(options, HttpContext.RequestAborted);
            using var tokenResponse = await client.PostAsync(endpoints.TokenEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["client_id"] = options.ClientId,
                    ["redirect_uri"] = options.CallbackUrl,
                    ["code_verifier"] = loginState.Verifier,
                }), HttpContext.RequestAborted);
            tokenResponse.EnsureSuccessStatusCode();
            var token = JsonSerializer.Deserialize<TokenResponse>(
                await tokenResponse.Content.ReadAsStringAsync(HttpContext.RequestAborted));
            if (string.IsNullOrWhiteSpace(token?.AccessToken)) throw new JsonException("Missing access token.");

            using var identityRequest = new HttpRequestMessage(HttpMethod.Get, endpoints.UserInfoEndpoint);
            identityRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            using var identityResponse = await client.SendAsync(identityRequest, HttpContext.RequestAborted);
            identityResponse.EnsureSuccessStatusCode();
            var identity = JsonSerializer.Deserialize<SsoIdentity>(
                await identityResponse.Content.ReadAsStringAsync(HttpContext.RequestAborted));
            if (identity is null || string.IsNullOrWhiteSpace(identity.Subject)
                || string.IsNullOrWhiteSpace(identity.Email))
                throw new JsonException("The identity response is incomplete.");

            var user = await _auth.GetOrCreateExternalUserAsync(
                identity.Email, identity.Name ?? "", UserRole.Viewer, HttpContext.RequestAborted);
            await AuthController.SignInAsync(HttpContext, user);
            return Redirect(loginState.ReturnUrl);
        }
        catch (HttpRequestException)
        {
            return LoginError("External sign-in could not be completed.");
        }
        catch (JsonException)
        {
            return LoginError("External sign-in returned an invalid identity.");
        }
        catch (ArgumentException)
        {
            return LoginError("External sign-in returned an invalid identity.");
        }
    }

    private SsoOptions? GetOptions()
    {
        var section = _configuration.GetSection("PlaceContext:Sso");
        var baseUrl = section["Authority"];
        var clientId = section["ClientId"];
        var callbackUrl = section["CallbackUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(clientId)
            || !Uri.TryCreate(callbackUrl, UriKind.Absolute, out var callbackUri)
            || callbackUri.Scheme != Uri.UriSchemeHttps)
            return null;
        var protocol = section["Protocol"]?.Trim();
        return new SsoOptions(
            baseUri,
            clientId,
            callbackUri.ToString(),
            string.Equals(protocol, "OpenIdConnect", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<SsoEndpoints> ResolveEndpointsAsync(SsoOptions options, CancellationToken ct)
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
            throw new JsonException("OIDC discovery metadata is incomplete.");

        return new SsoEndpoints(
            metadata.AuthorizationEndpoint,
            metadata.TokenEndpoint,
            metadata.UserInfoEndpoint,
            "openid email profile");
    }

    private static bool IsHttpsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private LoginState? ReadState()
    {
        if (!Request.Cookies.TryGetValue(StateCookie, out var protectedState)) return null;
        try
        {
            return JsonSerializer.Deserialize<LoginState>(_protector.Unprotect(protectedState));
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private IActionResult LoginError(string message) =>
        Redirect($"/login?error={Uri.EscapeDataString(message)}");

    private static bool SecureEquals(string expected, string? supplied)
    {
        if (string.IsNullOrEmpty(supplied)) return false;
        var left = Encoding.UTF8.GetBytes(expected);
        var right = Encoding.UTF8.GetBytes(supplied);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static string RandomToken(int bytes) =>
        Base64UrlTextEncoder.Encode(RandomNumberGenerator.GetBytes(bytes));

    private static string LocalOrHome(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl)
        && Uri.TryCreate(returnUrl, UriKind.Relative, out _)
        && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\")
            ? returnUrl : "/";

    private sealed record SsoOptions(Uri BaseUri, string ClientId, string CallbackUrl, bool OpenIdConnect);
    private sealed record SsoEndpoints(
        string AuthorizationEndpoint,
        string TokenEndpoint,
        string UserInfoEndpoint,
        string Scope);
    private sealed record OpenIdMetadata(
        [property: JsonPropertyName("authorization_endpoint")] string AuthorizationEndpoint,
        [property: JsonPropertyName("token_endpoint")] string TokenEndpoint,
        [property: JsonPropertyName("userinfo_endpoint")] string UserInfoEndpoint);
    private sealed record LoginState(string Nonce, string Verifier, string ReturnUrl, DateTimeOffset IssuedAt);
    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);
    private sealed record SsoIdentity(
        [property: JsonPropertyName("sub")] string Subject,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("name")] string? Name);
}
