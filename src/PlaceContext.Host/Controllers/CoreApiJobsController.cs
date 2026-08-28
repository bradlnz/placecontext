using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Host.Api;
using PlaceContext.Host.Auth;
using PlaceContext.Host.CoreApi;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Route("api/core/v1/projects/{projectId:guid}")]
[Authorize(AuthenticationSchemes = CoreApiAuthenticationHandler.SchemeName)]
[Produces("application/json")]
public sealed class CoreApiJobsController : ControllerBase
{
    private readonly PlaceContextService _svc;
    private readonly ICoreApiResourceResolver _resource;

    public CoreApiJobsController(PlaceContextService svc, ICoreApiResourceResolver resource)
    {
        _svc = svc;
        _resource = resource;
    }

    [HttpGet("jobs")]
    [Authorize(Policy = CoreApiScopes.JobsRead)]
    public async Task<ActionResult<IReadOnlyList<CoreJobSummaryResponse>>> ListJobs(Guid projectId)
    {
        if (!await ProjectExistsAsync(projectId))
            return NotFound(new { error = "Project not found." });

        var jobs = await _svc.ListJobsAsync(projectId, HttpContext.RequestAborted);
        return Ok(jobs.Select(CoreApiMapper.ToSummary).ToList());
    }

    [HttpGet("jobs/{jobId:guid}")]
    [Authorize(Policy = CoreApiScopes.JobsRead)]
    public async Task<ActionResult<CoreJobResponse>> GetJob(Guid projectId, Guid jobId)
    {
        var job = await _resource.GetJobAsync(projectId, jobId, HttpContext.RequestAborted);
        if (job is null)
            return NotFound(new { error = "Job not found in this project." });

        return Ok(CoreApiMapper.ToResponse(job));
    }

    [HttpPost("jobs")]
    [Authorize(Policy = CoreApiScopes.JobsWrite)]
    public async Task<ActionResult<CoreJobResponse>> CreateJob(Guid projectId, [FromBody] JobRequest request)
    {
        if (!await ProjectExistsAsync(projectId))
            return NotFound(new { error = "Project not found." });

        try
        {
            var job = await _svc.CreateJobAsync(JobApiMapper.ToCreateCommand(projectId, request), HttpContext.RequestAborted);
            return CreatedAtAction(nameof(GetJob), new { projectId, jobId = job.Id }, CoreApiMapper.ToResponse(job));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("jobs/{jobId:guid}/run")]
    [Authorize(Policy = CoreApiScopes.JobsRun)]
    public async Task<ActionResult<CoreJobRunDetailResponse>> RunJob(
        Guid projectId,
        Guid jobId,
        [FromBody] CoreRunJobRequest request)
    {
        var job = await _resource.GetJobAsync(projectId, jobId, HttpContext.RequestAborted);
        if (job is null)
            return NotFound(new { error = "Job not found in this project." });

        var run = await _svc.RunJobAsync(jobId, request.InputPayload, request.RunId, HttpContext.RequestAborted);
        return Ok(CoreApiMapper.ToResponse(run));
    }

    [HttpGet("jobs/{jobId:guid}/runs")]
    [Authorize(Policy = CoreApiScopes.JobsRead)]
    public async Task<ActionResult<IReadOnlyList<CoreJobRunSummaryResponse>>> ListRuns(
        Guid projectId,
        Guid jobId,
        [FromQuery] int take = 20)
    {
        if (!await ProjectExistsAsync(projectId))
            return NotFound(new { error = "Project not found." });

        var job = await _resource.GetJobAsync(projectId, jobId, HttpContext.RequestAborted);
        if (job is null)
            return NotFound(new { error = "Job not found in this project." });

        var runs = await _svc.ListJobRunsAsync(jobId, HttpContext.RequestAborted);
        return Ok(runs.Select(CoreApiMapper.ToResponse).ToList());
    }

    [HttpGet("jobs/{jobId:guid}/runs/{runId:guid}")]
    [Authorize(Policy = CoreApiScopes.JobsRead)]
    public async Task<ActionResult<CoreJobRunDetailResponse>> GetRun(Guid projectId, Guid jobId, Guid runId)
    {
        if (!await ProjectExistsAsync(projectId))
            return NotFound(new { error = "Project not found." });

        var run = await _svc.GetJobRunAsync(runId, HttpContext.RequestAborted);
        if (run is null || run.JobId != jobId)
            return NotFound(new { error = "Run not found for this job." });

        return Ok(CoreApiMapper.ToResponse(run));
    }

    [HttpPost("jobs/{jobId:guid}/runs/{runId:guid}/cancel")]
    [Authorize(Policy = CoreApiScopes.JobsRun)]
    public async Task<ActionResult<object>> CancelRun(Guid projectId, Guid jobId, Guid runId)
    {
        if (!await ProjectExistsAsync(projectId))
            return NotFound(new { error = "Project not found." });

        var run = await _svc.GetJobRunAsync(runId, HttpContext.RequestAborted);
        if (run is null || run.JobId != jobId)
            return NotFound(new { error = "Run not found for this job." });

        var cancelled = await _svc.CancelJobRunAsync(runId, HttpContext.RequestAborted);
        return Ok(new { cancelled });
    }

    private async Task<bool> ProjectExistsAsync(Guid projectId)
    {
        return await _resource.GetProjectAsync(projectId, HttpContext.RequestAborted) is not null;
    }
}
