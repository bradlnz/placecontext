using System.Globalization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Api;
using PlaceContext.Host.Controllers.Api.Records;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/job-page")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = Permission.JobsView)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class JobsPageController(IPlaceContextService placeContextService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<JobsPageResponse>> Get(Guid projectId, CancellationToken cancellationToken)
    {
        var jobsTask = placeContextService.ListJobsAsync(projectId, cancellationToken);
        var triggersTask = placeContextService.ListTriggersAsync(projectId, cancellationToken);
        await Task.WhenAll(jobsTask, triggersTask);
        return Ok(new JobsPageResponse(
            (await jobsTask).Select(JobApiMapper.ToResponse).ToList(),
            (await triggersTask).Select(trigger => new JobsPageTriggerResponse(
                trigger.Id, trigger.JobId, trigger.Name, trigger.Kind, trigger.Enabled,
                trigger.CronExpression, trigger.EventName)).ToList()));
    }

    [HttpPost("jobs")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<ActionResult<JobResponse>> Create(
        Guid projectId, [FromBody] JobRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(JobApiMapper.ToResponse(await placeContextService.CreateJobAsync(
                JobApiMapper.ToCreateCommand(projectId, request), cancellationToken)));
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpPut("jobs/{jobId:guid}")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<ActionResult<JobResponse>> Update(
        Guid projectId, Guid jobId, [FromBody] JobRequest request, CancellationToken cancellationToken)
    {
        var existing = await placeContextService.GetJobAsync(jobId, cancellationToken);
        if (existing is null || existing.ProjectId != projectId)
            return NotFound(new { error = "The job does not exist." });
        try
        {
            return Ok(JobApiMapper.ToResponse(await placeContextService.UpdateJobAsync(
                JobApiMapper.ToUpdateCommand(jobId, request), cancellationToken)));
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpDelete("jobs/{jobId:guid}")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<IActionResult> Delete(Guid projectId, Guid jobId, CancellationToken cancellationToken)
    {
        var existing = await placeContextService.GetJobAsync(jobId, cancellationToken);
        if (existing is null || existing.ProjectId != projectId)
            return NotFound(new { error = "The job does not exist." });
        return await placeContextService.DeleteJobAsync(jobId, cancellationToken)
            ? NoContent()
            : NotFound(new { error = "The job does not exist." });
    }

    [HttpGet("jobs/{jobId:guid}/runs")]
    public async Task<ActionResult<IReadOnlyList<JobRunPageResponse>>> Runs(
        Guid projectId, Guid jobId, CancellationToken cancellationToken)
    {
        var existing = await placeContextService.GetJobAsync(jobId, cancellationToken);
        if (existing is null || existing.ProjectId != projectId)
            return NotFound(new { error = "The job does not exist." });
        return Ok((await placeContextService.ListJobRunsAsync(jobId, cancellationToken)).Select(MapRun).ToList());
    }

    [HttpPost("jobs/{jobId:guid}/runs")]
    [Authorize(Policy = Permission.JobsManage)]
    public async Task<ActionResult<JobRunDetailPageResponse>> Run(
        Guid projectId, Guid jobId, [FromBody] RunJobPageRequest? request,
        CancellationToken cancellationToken)
    {
        var existing = await placeContextService.GetJobAsync(jobId, cancellationToken);
        if (existing is null || existing.ProjectId != projectId)
            return NotFound(new { error = "The job does not exist." });
        try { return Ok(MapDetail(await placeContextService.RunJobAsync(jobId, request?.InputPayload, ct: cancellationToken))); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpGet("runs/{runId:guid}")]
    public async Task<ActionResult<JobRunDetailPageResponse>> RunDetail(
        Guid projectId, Guid runId, CancellationToken cancellationToken)
    {
        var run = await placeContextService.GetJobRunAsync(runId, cancellationToken);
        return run is null || run.ProjectId != projectId
            ? NotFound(new { error = "The run does not exist." })
            : Ok(MapDetail(run));
    }

    [HttpPost("runs/{runId:guid}/cancel")]
    [Authorize(Policy = Permission.JobsManage)]
    public async Task<IActionResult> Cancel(Guid projectId, Guid runId, CancellationToken cancellationToken)
    {
        var run = await placeContextService.GetJobRunAsync(runId, cancellationToken);
        if (run is null || run.ProjectId != projectId)
            return NotFound(new { error = "The run does not exist." });
        await placeContextService.CancelJobRunAsync(runId, cancellationToken);
        return NoContent();
    }

    internal static JobRunDetailPageResponse MapDetail(JobRunDetailView run) => new(
        run.Id, run.JobId, run.Status, run.StartedAt, run.FinishedAt, run.AttemptNumber,
        run.OriginalRunId, run.ShardResults.Select(shard => new JobRunShardPageResponse(
            shard.Index, shard.ExitCode, shard.Outcome, shard.Artifact, shard.Log)).ToList());

    private static JobRunPageResponse MapRun(JobRunView run) => new(
        run.Id, run.JobId, run.Status, run.StartedAt, run.FinishedAt,
        run.StartedAt.ToWorkspaceTime().ToString("MMM d · HH:mm", CultureInfo.InvariantCulture),
        run.FinishedAt is { } finished ? Duration(run.StartedAt, finished) : null,
        run.ShardCount, run.SucceededShards, run.PartialShards, run.FailedShards);

    private static string Duration(DateTimeOffset start, DateTimeOffset finish)
    {
        var elapsed = finish - start;
        return elapsed.TotalSeconds < 1
            ? $"{elapsed.TotalMilliseconds:0} ms"
            : $"{elapsed.TotalSeconds:0.0} s";
    }
}
