using Microsoft.AspNetCore.Authorization;

namespace PlaceContext.Host.Auth;

/// <summary>Authorization requirement that succeeds for Blazor SignalR hub paths (/_blazor/*),
/// bypassing the FallbackPolicy's RequireAuthenticatedUser so the portal shell can load
/// even when the visitor is not signed in (e.g. the /login page).</summary>
public sealed class BlazorHubBypass : IAuthorizationRequirement { }
