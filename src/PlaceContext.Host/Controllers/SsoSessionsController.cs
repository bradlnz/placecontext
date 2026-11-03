using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using PlaceContext.Application.Access;
using PlaceContext.Application.Auth;
namespace PlaceContext.Host.Controllers;

/// <summary>Provider-neutral OAuth 2.1/PKCE login backed by a configured identity authority.</summary>
[AllowAnonymous]
public sealed class SsoSessionsController : ControllerBase
{
    private const string StateCookie = "placecontext_external_sso";
    private readonly IAuthService _auth;
    private readonly IConfiguration _configuration;
    private readonly IExternalIdentityProvider _identity;
    private readonly IDataProtector _protector;

    public SsoSessionsController(
        IAuthService auth,
        IConfiguration configuration,
        IExternalIdentityProvider identity,
        IDataProtectionProvider dataProtection)
    {
        _auth = auth;
        _configuration = configuration;
        _identity = identity;
        _protector = dataProtection.CreateProtector("PlaceContext.ExternalSso.State.v1");
    }

    [HttpGet("/auth/sso")]
    public async Task<IActionResult> New(string? returnUrl)
    {
        var options = GetOptions();
        if (options is null)
        {
            return StatusCode(503, "External sign-in is not configured.");
        }
        SsoEndpoints endpoints;
        try
        {
            endpoints = await _identity.ResolveEndpointsAsync(options, HttpContext.RequestAborted);
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
        {
            return LoginError("External sign-in expired. Please try again.");
        }
        try
        {
            var identity = await _identity.ExchangeAsync(options, code, loginState.Verifier, HttpContext.RequestAborted);

            var user = await _auth.GetOrCreateExternalUserAsync(
                identity.Email, identity.Name ?? "", ExternalRole(identity.Role), HttpContext.RequestAborted);
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

    internal static UserRole ExternalRole(string? role)
        => role?.Trim().ToLowerInvariant() is "admin" or "administrator" or "owner"
            ? UserRole.Admin : UserRole.Viewer;

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
        {
            return null;
        }
        var protocol = section["Protocol"]?.Trim();
        return new SsoOptions(
            baseUri,
            clientId,
            callbackUri.ToString(),
            string.Equals(protocol, "OpenIdConnect", StringComparison.OrdinalIgnoreCase));
    }

    private LoginState? ReadState()
    {
        if (!Request.Cookies.TryGetValue(StateCookie, out var protectedState))
        {
            return null;
        }
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
        if (string.IsNullOrEmpty(supplied))
        {
            return false;
        }
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

    private sealed record LoginState(string Nonce, string Verifier, string ReturnUrl, DateTimeOffset IssuedAt);
}
