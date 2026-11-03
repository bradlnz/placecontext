using System.Security.Claims;
using System.Text.Encodings.Web;
using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
namespace PlaceContext.Host.Auth;

/// <summary>Options bag for the "ApiKey" scheme — no per-request state, but AuthenticationHandler
/// requires an options type.</summary>
public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
}
