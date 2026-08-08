using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// Browser-facing read API for the incrementally migrated React portal. It deliberately uses only
/// cookie authentication: machine clients continue to use the separately versioned management and
/// core APIs, while the browser keeps credentials in the existing HTTP-only portal cookie.
/// </summary>
[ApiController]
[Route("api/v1/workspace")]
[Authorize(
    AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme,
    Policy = Permission.ProjectsView)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class WorkspaceController(
    IPlaceContextService placeContextService,
    ICurrentTenant currentTenant) : ControllerBase
{
    [HttpGet("projects")]
    public async Task<ActionResult<IReadOnlyList<ProjectSummaryView>>> Projects(
        CancellationToken cancellationToken)
        => Ok(await placeContextService.GetProjectsAsync(cancellationToken));

    [HttpGet("focus")]
    public async Task<ActionResult<FocusView>> Focus(CancellationToken cancellationToken)
        => Ok(await placeContextService.GetFocusAsync(cancellationToken));

    [HttpGet("stats")]
    public async Task<ActionResult<RootStatsView>> Stats(CancellationToken cancellationToken)
        => Ok(await placeContextService.GetRootStatsAsync(cancellationToken));

    [HttpGet("session")]
    public Task<ActionResult<SessionResponse>> Session()
        => Task.FromResult<ActionResult<SessionResponse>>(Ok(new SessionResponse(
            User.Identity?.Name ?? "PlaceContext user",
            User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "Viewer",
            currentTenant.Slug)));
}
