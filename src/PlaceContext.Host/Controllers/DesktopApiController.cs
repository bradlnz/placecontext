using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Api;
using PlaceContext.Host.CoreApi;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// User-scoped REST surface for the native desktop client. Unlike the machine-oriented Core API,
/// these routes accept an OAuth PKCE access token and enforce the signed-in member's permissions.
/// </summary>
[ApiController]
[Route("api/desktop")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "DesktopApi")]
[Produces("application/json")]
public sealed class DesktopApiController : ControllerBase
{
    private readonly IPlaceContextService _service;
    private readonly ICoreApiResourceResolver _resources;
    private readonly ICurrentTenant _tenant;

    public DesktopApiController(
        IPlaceContextService service,
        ICoreApiResourceResolver resources,
        ICurrentTenant tenant)
    {
        _service = service;
        _resources = resources;
        _tenant = tenant;
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(new
    {
        ok = true,
        api = "desktop",
        tenant = new { resolved = _tenant.IsResolved, id = _tenant.TenantId, slug = _tenant.Slug },
        userId = User.FindFirst("sub")?.Value,
        role = User.FindFirst("role")?.Value,
        issuedAt = DateTimeOffset.UtcNow,
    });

    [HttpGet("v1/projects")]
    [Authorize(Policy = Permission.ProjectsView)]
    public async Task<ActionResult<IReadOnlyList<CoreProjectResponse>>> ListProjects()
    {
        var projects = await _service.GetProjectsAsync(HttpContext.RequestAborted);
        return Ok(projects.Select(CoreApiMapper.ToResponse).ToList());
    }

    [HttpGet("v1/projects/{projectId:guid}/jobs")]
    [Authorize(Policy = Permission.JobsView)]
    public async Task<ActionResult<IReadOnlyList<CoreJobSummaryResponse>>> ListJobs(Guid projectId)
    {
        if (await _resources.GetProjectAsync(projectId, HttpContext.RequestAborted) is null)
            return NotFound(new { error = "Project not found." });

        var jobs = await _service.ListJobsAsync(projectId, HttpContext.RequestAborted);
        return Ok(jobs.Select(CoreApiMapper.ToSummary).ToList());
    }

    [HttpGet("v1/projects/{projectId:guid}/jobs/{jobId:guid}/runs")]
    [Authorize(Policy = Permission.JobsView)]
    public async Task<ActionResult<IReadOnlyList<CoreJobRunSummaryResponse>>> ListRuns(
        Guid projectId,
        Guid jobId,
        [FromQuery] int take = 10)
    {
        if (await _resources.GetJobAsync(projectId, jobId, HttpContext.RequestAborted) is null)
            return NotFound(new { error = "Job not found in this project." });

        var limit = Math.Clamp(take, 1, 100);
        var runs = await _service.ListJobRunsAsync(jobId, HttpContext.RequestAborted);
        return Ok(runs.Take(limit).Select(CoreApiMapper.ToResponse).ToList());
    }
}
