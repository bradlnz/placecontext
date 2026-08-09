using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Jobs.Contracts.Api;

namespace PlaceContext.Jobs.Controllers;

[ApiController]
[Route("api/jobs/projects/{projectId:guid}/schedule-page")]
[Authorize(Policy = Permission.TriggersManage)]
[Produces("application/json")]
public sealed class SchedulesPageController(IDispatcher dispatcher, ICurrentTenant currentTenant)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SchedulePageResponse>> Get(Guid projectId, CancellationToken ct)
    {
        var jobsTask = dispatcher.Query(new ListJobsQuery(projectId), ct);
        var chainsTask = dispatcher.Query(new ListJobChainsQuery(projectId), ct);
        var triggersTask = dispatcher.Query(new ListTriggersQuery(projectId), ct);
        var eventsTask = dispatcher.Query(new ListEventTypesQuery(), ct);
        await Task.WhenAll(jobsTask, chainsTask, triggersTask, eventsTask);
        var jobs = await jobsTask;
        var chains = await chainsTask;

        return Ok(new SchedulePageResponse(
            currentTenant.TimeZoneId,
            jobs.Select(job => new ScheduleTargetResponse(job.Id, job.Name)).ToList(),
            chains.Select(chain => new ScheduleTargetResponse(chain.Id, chain.Name)).ToList(),
            [],
            (await eventsTask).Select(type => type.Name).ToList(),
            (await triggersTask).Select(trigger => Map(trigger, jobs, chains)).ToList()));
    }

    [HttpPost("triggers")]
    public async Task<ActionResult<ScheduleTriggerResponse>> Create(
        Guid projectId,
        CreateSchedulePageTriggerRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required." });

        try
        {
            var trigger = await dispatcher.Send(new CreateTriggerCommand(
                request.JobId,
                request.Name.Trim(),
                request.Kind,
                request.CronExpression,
                request.EventName,
                request.ChainId,
                request.SourceTable,
                request.Prompt), ct);
            return Ok(await MapWithTargets(projectId, trigger, ct));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPut("triggers/{triggerId:guid}")]
    public async Task<ActionResult<ScheduleTriggerResponse>> Update(
        Guid projectId,
        Guid triggerId,
        UpdateSchedulePageTriggerRequest request,
        CancellationToken ct)
    {
        try
        {
            var trigger = await dispatcher.Send(new UpdateTriggerCommand(
                triggerId,
                request.Name,
                request.CronExpression,
                request.EventName,
                request.Enabled), ct);
            return Ok(await MapWithTargets(projectId, trigger, ct));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return NotFound(new { error = exception.Message });
        }
    }

    [HttpDelete("triggers/{triggerId:guid}")]
    public async Task<IActionResult> Delete(Guid triggerId, CancellationToken ct)
        => await dispatcher.Send(new DeleteTriggerCommand(triggerId), ct)
            ? NoContent()
            : NotFound(new { error = "The trigger does not exist." });

    private async Task<ScheduleTriggerResponse> MapWithTargets(
        Guid projectId,
        TriggerView trigger,
        CancellationToken ct)
    {
        var jobsTask = dispatcher.Query(new ListJobsQuery(projectId), ct);
        var chainsTask = dispatcher.Query(new ListJobChainsQuery(projectId), ct);
        await Task.WhenAll(jobsTask, chainsTask);
        return Map(trigger, await jobsTask, await chainsTask);
    }

    private ScheduleTriggerResponse Map(
        TriggerView trigger,
        IReadOnlyList<JobView> jobs,
        IReadOnlyList<JobChainView> chains)
    {
        var target = trigger.Kind.Equals("Launchpad", StringComparison.OrdinalIgnoreCase)
            ? (chains.FirstOrDefault(chain => chain.Id == trigger.ChainId)?.Name ?? "Deleted chain")
                + (string.IsNullOrEmpty(trigger.SourceTable) ? string.Empty : $" · {trigger.SourceTable}")
            : jobs.FirstOrDefault(job => job.Id == trigger.JobId)?.Name ?? "Deleted job";
        return new ScheduleTriggerResponse(
            trigger.Id,
            trigger.Name,
            trigger.Kind,
            trigger.Enabled,
            trigger.CronExpression,
            trigger.EventName,
            trigger.JobId,
            trigger.ChainId,
            trigger.SourceTable,
            trigger.Prompt,
            target,
            trigger.NextRunAt is { } next ? ShortDateTime(next) : "—",
            trigger.LastFiredAt is { } last ? ShortDateTime(last) : "never");
    }

    private string ShortDateTime(DateTimeOffset value)
        => TimeZoneInfo.ConvertTime(
                value,
                TimeZoneInfo.FindSystemTimeZoneById(currentTenant.TimeZoneId))
            .ToString("MMM d · HH:mm");
}
