using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Api;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers.Api;

/// <summary>
/// Management API: projects — the machine-facing contract the Terraform provider (and any other IaC/CI
/// client) declares projects against. See docs/management-api.md for the full reference. Authenticated
/// via the ApiKey scheme only (never the portal cookie or the MCP bearer scheme — see
/// <see cref="ApiKeyAuthenticationHandler"/>), gated per-action by the projects.* fine-grained
/// permissions. Tenant scoping is automatic: <c>TenantResolutionMiddleware</c> resolves the workspace
/// from the request subdomain before this controller runs, and every repository call below is filtered
/// to that tenant by EF's global query filter — a key on one subdomain can never see or mutate another
/// tenant's projects.
///
/// Projects have no delete path in the domain (only <c>Project.Archive()</c>, which nothing currently
/// exposes end-to-end) — so, per the brief, there is deliberately no DELETE here.
/// </summary>
[ApiController]
[Route("api/v1/projects")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
[Produces("application/json")]
public sealed class ProjectsController : ControllerBase
{
    private readonly PlaceContextService _svc;
    public ProjectsController(PlaceContextService svc) => _svc = svc;

    /// <summary>GET /api/v1/projects — every project known to this workspace.</summary>
    [HttpGet]
    [Authorize(Policy = Permission.ProjectsView)]
    public async Task<ActionResult<IReadOnlyList<ProjectResponse>>> List()
    {
        var projects = await _svc.GetProjectsAsync(HttpContext.RequestAborted);
        return Ok(projects.Select(ProjectApiMapper.ToResponse).ToList());
    }

    /// <summary>GET /api/v1/projects/{id} — a single project, or 404 when it doesn't exist.</summary>
    [HttpGet("{id:guid}", Name = "GetProjectById")]
    [Authorize(Policy = Permission.ProjectsView)]
    public async Task<ActionResult<ProjectResponse>> GetById(Guid id)
    {
        var project = await _svc.GetProjectByIdAsync(id, HttpContext.RequestAborted);
        return project is null ? NotFound() : Ok(ProjectApiMapper.ToResponse(project));
    }

    /// <summary>
    /// POST /api/v1/projects — registers a new project. Idempotent by path: re-posting a known path
    /// returns the existing project (still 201, matching the underlying command's idempotent-create
    /// contract — safe for Terraform to retry a create that actually already landed).
    /// </summary>
    [HttpPost]
    [Authorize(Policy = Permission.ProjectsManage)]
    public async Task<ActionResult<ProjectResponse>> Create([FromBody] CreateProjectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
            return BadRequest(new { error = "path is required." });

        var project = await _svc.CreateProjectAsync(request.Path, request.Name, HttpContext.RequestAborted);
        var response = ProjectApiMapper.ToResponse(project);
        return CreatedAtRoute("GetProjectById", new { id = response.Id }, response);
    }
}
