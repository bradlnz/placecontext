using System.Globalization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Controllers.Api.Records;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/event-page")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = Permission.EventsManage)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class EventsPageController(IPlaceContextService placeContextService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<EventsPageResponse>> Get(Guid projectId, CancellationToken cancellationToken)
    {
        var typesTask = placeContextService.ListEventTypesAsync(cancellationToken);
        var logTask = placeContextService.ListEventOccurrencesAsync(50, cancellationToken);
        var triggersTask = placeContextService.ListTriggersAsync(projectId, cancellationToken);
        await Task.WhenAll(typesTask, logTask, triggersTask);
        return Ok(new EventsPageResponse(
            (await typesTask).Select(type => new EventTypePageResponse(type.Name, type.Description, type.IsBuiltIn, type.PayloadSchema)).ToList(),
            (await logTask).Select(item => new EventOccurrencePageResponse(
                item.Id, item.Name, item.Source,
                item.Source.Equals("Domain", StringComparison.OrdinalIgnoreCase) ? "system" : "manual",
                item.Payload, item.OccurredAt,
                item.OccurredAt.ToWorkspaceTime().ToString("MMM d, yyyy · HH:mm", CultureInfo.InvariantCulture),
                item.TriggeredRuns)).ToList(),
            (await triggersTask).Where(trigger => trigger.Kind == "Event").Select(trigger => new EventSubscriptionPageResponse(trigger.Id, trigger.EventName, trigger.Enabled)).ToList()));
    }

    [HttpPost("types")]
    public async Task<ActionResult<EventTypePageResponse>> Define(
        Guid projectId, [FromBody] DefineEventTypePageRequest request, CancellationToken cancellationToken)
    {
        _ = projectId;
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { error = "Name is required." });
        try
        {
            var type = await placeContextService.DefineEventTypeAsync(
                request.Name.Trim(), Clean(request.Description), Clean(request.PayloadSchema), cancellationToken);
            return Ok(new EventTypePageResponse(type.Name, type.Description, type.IsBuiltIn, type.PayloadSchema));
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpPost("types/{name}/occurrences")]
    public async Task<ActionResult<EmitEventPageResponse>> Emit(
        Guid projectId, string name, [FromBody] EmitEventPageRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await placeContextService.EmitEventAsync(name, projectId, Clean(request.Payload), cancellationToken);
            return Ok(new EmitEventPageResponse(result.TriggeredRuns));
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return BadRequest(new { error = exception.Message }); }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
