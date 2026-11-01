using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Connections;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// Authorisation code callback. Enforces the same authenticated administrator that started the
/// one-time, PKCE-protected request. Never returns credentials or the auth code to the UI.
/// </summary>
[Route("api/managed-oauth/microsoft")]
[Authorize(Policy = Policies.DefaultAdmin)]
public sealed class ManagedMicrosoftOAuthController(IManagedOAuthConnections connections) : Controller
{
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? state, [FromQuery] string? code,
        [FromQuery] string? error, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(error) || string.IsNullOrWhiteSpace(state)
            || string.IsNullOrWhiteSpace(code))
            return Redirect("/settings/connections?oauth=failed");
        try
        {
            var project = await connections.CompleteAsync(state, code, ct);
            return Redirect($"/settings/connections?project={project:D}&oauth=connected");
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (Exception) { return Redirect("/settings/connections?oauth=failed"); }
    }
}
