using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Observability;
using PlaceContext.Application.Ports;
using PlaceContext.Jobs.Contracts.Api;

namespace PlaceContext.Jobs.Controllers;

[ApiController]
[Route("api/jobs")]
[Authorize(Policy = Permission.JobsView)]
[Produces("application/json")]
public sealed class JobsController(IDispatcher dispatcher, IJobTelemetryReader telemetry) : ControllerBase
{
    [HttpGet("projects/{projectId:guid}")]
    public async Task<IActionResult> List(Guid projectId, CancellationToken ct)
        => Ok(await dispatcher.Query(new ListJobsQuery(projectId), ct));

    [HttpGet("{jobId:guid}")]
    public async Task<IActionResult> Get(Guid jobId, CancellationToken ct)
        => await dispatcher.Query(new GetJobQuery(jobId), ct) is { } job ? Ok(job) : NotFound();

    [HttpGet("projects/{projectId:guid}/chains")]
    public async Task<IActionResult> ListChains(Guid projectId, CancellationToken ct)
        => Ok(await dispatcher.Query(new ListJobChainsQuery(projectId), ct));

    [HttpGet("projects/{projectId:guid}/schedules")]
    [Authorize(Policy = Permission.TriggersManage)]
    public async Task<IActionResult> ListSchedules(Guid projectId, CancellationToken ct)
        => Ok(await dispatcher.Query(new ListTriggersQuery(projectId), ct));

    [HttpGet("observability")]
    public async Task<ActionResult<ObservabilityPageResponse>> Observability(CancellationToken ct)
    {
        var runsTask = dispatcher.Query(new ListRecentRunReportsQuery(50), ct);
        var chainsTask = dispatcher.Query(new ListRecentChainRunsQuery(50), ct);
        await Task.WhenAll(runsTask, chainsTask);
        return Ok(new ObservabilityPageResponse(
            await runsTask,
            await chainsTask,
            telemetry.RecentRuns(50),
            User.HasClaim("permission", Permission.JobsReplay)));
    }

    [HttpGet("observability/runs/{runId:guid}")]
    public async Task<ActionResult<ObservabilityRunDetailsResponse>> ObservabilityRun(
        Guid runId,
        [FromQuery] Guid jobId,
        CancellationToken ct)
    {
        var jobTelemetry = await dispatcher.Query(new ListJobRunTelemetryQuery(jobId, 50), ct);
        return Ok(new ObservabilityRunDetailsResponse(
            jobTelemetry.FirstOrDefault(item => item.RunId == runId),
            telemetry.TraceForRun(runId)));
    }

    [HttpPost("observability/runs/{runId:guid}/replay")]
    [Authorize(Policy = Permission.JobsReplay)]
    public async Task<ActionResult<ReplayObservabilityRunResponse>> ReplayObservabilityRun(
        Guid runId,
        CancellationToken ct)
    {
        try
        {
            var run = await dispatcher.Send(new ReplayRunCommand(runId, Guid.NewGuid()), ct);
            return Ok(new ReplayObservabilityRunResponse(run.Id, run.Status));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
