using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Jobs.Contracts.Api;

namespace PlaceContext.Jobs.Controllers;

[ApiController]
[Route("api/jobs/projects/{projectId:guid}/events")]
[Authorize(Policy = Permission.EventsManage)]
[Produces("application/json")]
public sealed class EventsController(IDispatcher dispatcher, ICurrentTenant currentTenant) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<EventsPageResponse>> Get(Guid projectId, CancellationToken ct)
    {
        var typesTask = dispatcher.Query(new ListEventTypesQuery(), ct);
        var logTask = dispatcher.Query(new ListEventOccurrencesQuery(50), ct);
        var triggersTask = dispatcher.Query(new ListTriggersQuery(projectId), ct);
        await Task.WhenAll(typesTask, logTask, triggersTask);

        return Ok(new EventsPageResponse(
            (await typesTask).Select(type => new EventTypePageResponse(
                type.Name, type.Description, type.IsBuiltIn, type.PayloadSchema)).ToList(),
            (await logTask).Select(item => new EventOccurrencePageResponse(
                item.Id,
                item.Name,
                item.Source,
                item.Source.Equals("Domain", StringComparison.OrdinalIgnoreCase) ? "system" : "manual",
                item.Payload,
                item.OccurredAt,
                WorkspaceTime(item.OccurredAt).ToString(
                    "MMM d, yyyy · HH:mm",
                    CultureInfo.InvariantCulture),
                item.TriggeredRuns)).ToList(),
            (await triggersTask)
                .Where(trigger => trigger.Kind == "Event")
                .Select(trigger => new EventSubscriptionPageResponse(
                    trigger.Id, trigger.EventName, trigger.Enabled))
                .ToList()));
    }

    [HttpPost("types")]
    public async Task<ActionResult<EventTypePageResponse>> Define(
        Guid projectId,
        DefineEventTypePageRequest request,
        CancellationToken ct)
    {
        _ = projectId;
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required." });

        try
        {
            var type = await dispatcher.Send(new DefineEventTypeCommand(
                request.Name.Trim(), Clean(request.Description), Clean(request.PayloadSchema)), ct);
            return Ok(new EventTypePageResponse(
                type.Name, type.Description, type.IsBuiltIn, type.PayloadSchema));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPost("types/{name}/occurrences")]
    public async Task<ActionResult<EmitEventPageResponse>> Emit(
        Guid projectId,
        string name,
        EmitEventPageRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await dispatcher.Send(
                new EmitEventCommand(name, projectId, Clean(request.Payload)), ct);
            return Ok(new EmitEventPageResponse(result.TriggeredRuns));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private DateTimeOffset WorkspaceTime(DateTimeOffset value)
        => TimeZoneInfo.ConvertTime(
            value,
            TimeZoneInfo.FindSystemTimeZoneById(currentTenant.TimeZoneId));
}
