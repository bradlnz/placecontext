using Microsoft.AspNetCore.Authentication;
namespace PlaceContext.Host.Auth;

/// <summary>Options bag for the "ApiKey" scheme — no per-request state, but AuthenticationHandler
/// requires an options type.</summary>
public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
}
