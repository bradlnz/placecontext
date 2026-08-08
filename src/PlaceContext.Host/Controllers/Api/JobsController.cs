using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Api;
using PlaceContext.Host.Auth;
using PlaceContext.Jobs.Contracts.Management;
using PlaceContext.Jobs.Management;

namespace PlaceContext.Host.Controllers.Api;

/// <summary>
/// Management API: job definitions — the machine-facing contract the Terraform provider declares
/// generic map/reduce jobs against. See docs/management-api.md. Authenticated via the ApiKey scheme only
/// (see <see cref="ApiKeyAuthenticationHandler"/>), gated by the jobs.* fine-grained permissions, and
/// implicitly tenant-scoped by <c>TenantResolutionMiddleware</c> + EF's global query filter.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
[Produces("application/json")]
public sealed class JobsController(IPlaceContextService placeContextService) : ControllerBase
{
    private readonly IPlaceContextService _placeContextService
        = placeContextService ?? throw new NullReferenceException($"Missing dependency {nameof(placeContextService)}");

    /// <summary>GET /api/v1/projects/{projectId}/jobs — every job defined under the project, or 404 if
    /// the project itself doesn't exist.</summary>
    [HttpGet("projects/{projectId:guid}/jobs")]
    [Authorize(Policy = Permission.JobsView)]
    public async Task<ActionResult<IReadOnlyList<JobResponse>>> ListForProject(Guid projectId)
    {
        var ct = HttpContext.RequestAborted;
        if (await _placeContextService.GetProjectByIdAsync(projectId, ct) is null) return NotFound();

        var jobs = await _placeContextService.ListJobsAsync(projectId, ct);
        return Ok(jobs.Select(JobApiMapper.ToResponse).ToList());
    }

    /// <summary>POST /api/v1/projects/{projectId}/jobs — creates a job definition under the project.
    /// 404 if the project doesn't exist; 400 on an invalid workload spec (e.g. neither image nor code
    /// source, or both).</summary>
    [HttpPost("projects/{projectId:guid}/jobs")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<ActionResult<JobResponse>> Create(Guid projectId, [FromBody] JobRequest request)
    {
        var ct = HttpContext.RequestAborted;
        if (await _placeContextService.GetProjectByIdAsync(projectId, ct) is null) return NotFound();

        try
        {
            var job = await _placeContextService.CreateJobAsync(JobApiMapper.ToCreateCommand(projectId, request), ct);
            var response = JobApiMapper.ToResponse(job);
            return CreatedAtRoute("GetJobById", new { id = response.Id }, response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

}
