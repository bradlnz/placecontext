using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Jobs.Contracts.Api;
using PlaceContext.Jobs.Contracts.Management;
using PlaceContext.Jobs.Management;

namespace PlaceContext.Jobs.Controllers;

[ApiController]
[Route("api/jobs/projects/{projectId:guid}/job-page")]
[Authorize(Policy = Permission.JobsView)]
[Produces("application/json")]
public sealed class JobsPageController(IDispatcher dispatcher, ICurrentTenant currentTenant)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<JobsPageResponse>> Get(Guid projectId, CancellationToken ct)
    {
        var jobsTask = dispatcher.Query(new ListJobsQuery(projectId), ct);
        var triggersTask = dispatcher.Query(new ListTriggersQuery(projectId), ct);
        await Task.WhenAll(jobsTask, triggersTask);
        return Ok(new JobsPageResponse(
            (await jobsTask).Select(JobApiMapper.ToResponse).ToList(),
            (await triggersTask).Select(trigger => new JobsPageTriggerResponse(
                trigger.Id,
                trigger.JobId,
                trigger.Name,
                trigger.Kind,
                trigger.Enabled,
                trigger.CronExpression,
                trigger.EventName)).ToList()));
    }

    [HttpPost("jobs")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<ActionResult<JobResponse>> Create(
        Guid projectId,
        JobRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(JobApiMapper.ToResponse(await dispatcher.Send(
                JobApiMapper.ToCreateCommand(projectId, request), ct)));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPut("jobs/{jobId:guid}")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<ActionResult<JobResponse>> Update(
        Guid projectId,
        Guid jobId,
        JobRequest request,
        CancellationToken ct)
    {
        if (!await JobBelongsToProject(jobId, projectId, ct))
            return NotFound(new { error = "The job does not exist." });
        try
        {
            return Ok(JobApiMapper.ToResponse(await dispatcher.Send(
                JobApiMapper.ToUpdateCommand(jobId, request), ct)));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpDelete("jobs/{jobId:guid}")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<IActionResult> Delete(Guid projectId, Guid jobId, CancellationToken ct)
    {
        if (!await JobBelongsToProject(jobId, projectId, ct))
            return NotFound(new { error = "The job does not exist." });
        return await dispatcher.Send(new DeleteJobCommand(jobId), ct)
            ? NoContent()
            : NotFound(new { error = "The job does not exist." });
    }

    [HttpGet("jobs/{jobId:guid}/runs")]
    public async Task<ActionResult<IReadOnlyList<JobRunPageResponse>>> Runs(
        Guid projectId,
        Guid jobId,
        CancellationToken ct)
    {
        if (!await JobBelongsToProject(jobId, projectId, ct))
            return NotFound(new { error = "The job does not exist." });
        return Ok((await dispatcher.Query(new ListJobRunsQuery(jobId), ct)).Select(MapRun).ToList());
    }

    [HttpPost("jobs/{jobId:guid}/runs")]
    [Authorize(Policy = Permission.JobsManage)]
    public async Task<ActionResult<JobRunDetailPageResponse>> Run(
        Guid projectId,
        Guid jobId,
        RunJobPageRequest? request,
        CancellationToken ct)
    {
        if (!await JobBelongsToProject(jobId, projectId, ct))
            return NotFound(new { error = "The job does not exist." });
        try
        {
            return Ok(MapDetail(await dispatcher.Send(
                new RunJobCommand(jobId, request?.InputPayload), ct)));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("runs/{runId:guid}")]
    public async Task<ActionResult<JobRunDetailPageResponse>> RunDetail(
        Guid projectId,
        Guid runId,
        CancellationToken ct)
    {
        var run = await dispatcher.Query(new GetJobRunQuery(runId), ct);
        return run is null || run.ProjectId != projectId
            ? NotFound(new { error = "The run does not exist." })
            : Ok(MapDetail(run));
    }

    [HttpPost("runs/{runId:guid}/cancel")]
    [Authorize(Policy = Permission.JobsManage)]
    public async Task<IActionResult> Cancel(Guid projectId, Guid runId, CancellationToken ct)
    {
        var run = await dispatcher.Query(new GetJobRunQuery(runId), ct);
        if (run is null || run.ProjectId != projectId)
            return NotFound(new { error = "The run does not exist." });
        await dispatcher.Send(new CancelJobRunCommand(runId), ct);
        return NoContent();
    }

    [HttpGet("jobs/{jobId:guid}/code-page")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<ActionResult<JobCodePageResponse>> GetCode(
        Guid projectId,
        Guid jobId,
        CancellationToken ct)
    {
        var job = await dispatcher.Query(new GetJobQuery(jobId), ct);
        return job is null || job.ProjectId != projectId
            ? NotFound(new { error = "The job does not exist." })
            : Ok(new JobCodePageResponse(JobApiMapper.ToResponse(job)));
    }

    [HttpPut("jobs/{jobId:guid}/code-page")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<ActionResult<JobResponse>> UpdateCode(
        Guid projectId,
        Guid jobId,
        UpdateJobCodePageRequest request,
        CancellationToken ct)
    {
        var saved = await SaveCode(projectId, jobId, request, ct);
        return saved is null
            ? NotFound(new { error = "The job does not exist." })
            : Ok(saved);
    }

    [HttpPost("jobs/{jobId:guid}/code-page/run")]
    [Authorize(Policy = Permission.JobsManage)]
    public async Task<ActionResult<RunJobCodePageResponse>> RunCode(
        Guid projectId,
        Guid jobId,
        UpdateJobCodePageRequest request,
        CancellationToken ct)
    {
        try
        {
            var saved = await SaveCode(projectId, jobId, request, ct);
            if (saved is null)
                return NotFound(new { error = "The job does not exist." });
            var run = await dispatcher.Send(new RunJobCommand(jobId), ct);
            return Ok(new RunJobCodePageResponse(saved, MapDetail(run)));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    private async Task<JobResponse?> SaveCode(
        Guid projectId,
        Guid jobId,
        UpdateJobCodePageRequest request,
        CancellationToken ct)
    {
        var job = await dispatcher.Query(new GetJobQuery(jobId), ct);
        if (job is null || job.ProjectId != projectId)
            return null;
        var updated = await dispatcher.Send(new UploadJobCodeCommand(
            jobId,
            projectId,
            job.Name,
            request.RuntimeId,
            request.Entrypoint,
            request.Files.Select(file => new CodeFileDto(file.Path, file.Content)).ToList()), ct);
        return JobApiMapper.ToResponse(updated);
    }

    private async Task<bool> JobBelongsToProject(Guid jobId, Guid projectId, CancellationToken ct)
        => await dispatcher.Query(new GetJobQuery(jobId), ct) is { ProjectId: var owner }
            && owner == projectId;

    internal static JobRunDetailPageResponse MapDetail(JobRunDetailView run) => new(
        run.Id,
        run.JobId,
        run.Status,
        run.StartedAt,
        run.FinishedAt,
        run.AttemptNumber,
        run.OriginalRunId,
        run.ShardResults.Select(shard => new JobRunShardPageResponse(
            shard.Index, shard.ExitCode, shard.Outcome, shard.Artifact, shard.Log)).ToList());

    private JobRunPageResponse MapRun(JobRunView run) => new(
        run.Id,
        run.JobId,
        run.Status,
        run.StartedAt,
        run.FinishedAt,
        WorkspaceTime(run.StartedAt).ToString("MMM d · HH:mm", CultureInfo.InvariantCulture),
        run.FinishedAt is { } finished ? Duration(run.StartedAt, finished) : null,
        run.ShardCount,
        run.SucceededShards,
        run.PartialShards,
        run.FailedShards);

    private DateTimeOffset WorkspaceTime(DateTimeOffset value)
        => TimeZoneInfo.ConvertTime(
            value,
            TimeZoneInfo.FindSystemTimeZoneById(currentTenant.TimeZoneId));

    private static string Duration(DateTimeOffset start, DateTimeOffset finish)
    {
        var elapsed = finish - start;
        return elapsed.TotalSeconds < 1
            ? $"{elapsed.TotalMilliseconds:0} ms"
            : $"{elapsed.TotalSeconds:0.0} s";
    }
}
