using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Controllers.Api.Records;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/schedule-page")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = Permission.TriggersManage)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class SchedulePageController(IPlaceContextService placeContextService, ICurrentTenant currentTenant) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SchedulePageResponse>> Get(Guid projectId, CancellationToken cancellationToken)
    {
        var jobsTask = placeContextService.ListJobsAsync(projectId, cancellationToken);
        var chainsTask = placeContextService.ListJobChainsAsync(projectId, cancellationToken);
        var tablesTask = placeContextService.ListProjectDataTablesAsync(projectId, cancellationToken);
        var triggersTask = placeContextService.ListTriggersAsync(projectId, cancellationToken);
        var eventsTask = placeContextService.ListEventTypesAsync(cancellationToken);
        await Task.WhenAll(jobsTask, chainsTask, tablesTask, triggersTask, eventsTask);
        var jobs = await jobsTask;
        var chains = await chainsTask;
        return Ok(new SchedulePageResponse(
            currentTenant.TimeZoneId,
            jobs.Select(job => new ScheduleTargetResponse(job.Id, job.Name)).ToList(),
            chains.Select(chain => new ScheduleTargetResponse(chain.Id, chain.Name)).ToList(),
            (await tablesTask).Select(table => table.Name).ToList(),
            (await eventsTask).Select(type => type.Name).ToList(),
            (await triggersTask).Select(trigger => Map(trigger, jobs, chains)).ToList()));
    }

    [HttpPost("triggers")]
    public async Task<ActionResult<ScheduleTriggerResponse>> Create(
        Guid projectId,
        [FromBody] CreateSchedulePageTriggerRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { error = "Name is required." });
        try
        {
            var trigger = await placeContextService.CreateTriggerAsync(new CreateTriggerCommand(
                request.JobId,
                request.Name.Trim(),
                request.Kind,
                request.CronExpression,
                request.EventName,
                request.ChainId,
                request.SourceTable,
                request.Prompt), cancellationToken);
            var jobs = await placeContextService.ListJobsAsync(projectId, cancellationToken);
            var chains = await placeContextService.ListJobChainsAsync(projectId, cancellationToken);
            return Ok(Map(trigger, jobs, chains));
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpPut("triggers/{triggerId:guid}")]
    public async Task<ActionResult<ScheduleTriggerResponse>> Update(
        Guid projectId,
        Guid triggerId,
        [FromBody] UpdateSchedulePageTriggerRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var trigger = await placeContextService.UpdateTriggerAsync(new UpdateTriggerCommand(
                triggerId, request.Name, request.CronExpression, request.EventName, request.Enabled), cancellationToken);
            var jobs = await placeContextService.ListJobsAsync(projectId, cancellationToken);
            var chains = await placeContextService.ListJobChainsAsync(projectId, cancellationToken);
            return Ok(Map(trigger, jobs, chains));
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return NotFound(new { error = exception.Message }); }
    }

    [HttpDelete("triggers/{triggerId:guid}")]
    public async Task<IActionResult> Delete(Guid triggerId, CancellationToken cancellationToken) =>
        await placeContextService.DeleteTriggerAsync(triggerId, cancellationToken)
            ? NoContent()
            : NotFound(new { error = "The trigger does not exist." });

    private static ScheduleTriggerResponse Map(
        TriggerView trigger,
        IReadOnlyList<JobView> jobs,
        IReadOnlyList<JobChainView> chains)
    {
        var target = trigger.Kind.Equals("Launchpad", StringComparison.OrdinalIgnoreCase)
            ? (chains.FirstOrDefault(chain => chain.Id == trigger.ChainId)?.Name ?? "Deleted chain")
                + (string.IsNullOrEmpty(trigger.SourceTable) ? string.Empty : $" · {trigger.SourceTable}")
            : jobs.FirstOrDefault(job => job.Id == trigger.JobId)?.Name ?? "Deleted job";
        return new ScheduleTriggerResponse(
            trigger.Id, trigger.Name, trigger.Kind, trigger.Enabled, trigger.CronExpression,
            trigger.EventName, trigger.JobId, trigger.ChainId, trigger.SourceTable, trigger.Prompt,
            target,
            trigger.NextRunAt is { } next ? ShortDateTime(next) : "—",
            trigger.LastFiredAt is { } last ? ShortDateTime(last) : "never");
    }

    private static string ShortDateTime(DateTimeOffset value) => value.ToWorkspaceTime().ToString("MMM d · HH:mm");
}
