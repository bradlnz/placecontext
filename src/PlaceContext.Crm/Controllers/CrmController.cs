using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Crm.Controllers;

[ApiController]
[Route("api/crm")]
[Authorize(Policy = Permission.CrmView)]
[Produces("application/json")]
public sealed class CrmController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("projects/{projectId:guid}/clients")]
    public async Task<IActionResult> ListClients(Guid projectId, CancellationToken ct)
        => Ok(await dispatcher.Query(new ListCrmClientsQuery(projectId), ct));

    [HttpGet("projects/{projectId:guid}/appointments")]
    public async Task<IActionResult> ListAppointments(Guid projectId, CancellationToken ct)
        => Ok(await dispatcher.Query(new ListCrmAppointmentsQuery(projectId), ct));

    [HttpGet("projects/{projectId:guid}/calendars")]
    public async Task<IActionResult> ListCalendars(Guid projectId, CancellationToken ct)
        => Ok(await dispatcher.Query(new ListCrmCalendarsQuery(projectId), ct));
}
