using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Security;

namespace PlaceContext.Host.Auth;

/// <summary>Options bag for the "ApiKey" scheme — no per-request state, but AuthenticationHandler
/// requires an options type.</summary>
public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
}
