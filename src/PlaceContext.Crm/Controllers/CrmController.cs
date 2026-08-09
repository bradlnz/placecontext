using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Contracts.Api;
using PlaceContext.Domain.ValueObjects;

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

    [HttpGet("projects/{projectId:guid}/automations")]
    public async Task<IActionResult> ListAutomations(Guid projectId, CancellationToken ct)
        => Ok(await dispatcher.Query(new ListCrmAutomationRulesQuery(projectId), ct));

    [HttpGet("communication-capabilities")]
    public async Task<IActionResult> CommunicationCapabilities(CancellationToken ct)
        => Ok(await dispatcher.Query(new GetCrmCommsCapabilitiesQuery(), ct));

    [HttpGet("clients/{clientId:guid}/runs")]
    public async Task<IActionResult> ListClientRuns(Guid clientId, CancellationToken ct)
        => Ok(await dispatcher.Query(new ListCrmClientChainRunsQuery(clientId, 20), ct));

    [HttpGet("clients/{clientId:guid}/communications")]
    public async Task<IActionResult> ListClientCommunications(Guid clientId, CancellationToken ct)
        => Ok(await dispatcher.Query(new ListCrmClientCommunicationsQuery(clientId, 100), ct));

    [HttpGet("clients/{clientId:guid}/artifacts")]
    public async Task<IActionResult> ListClientArtifacts(Guid clientId, CancellationToken ct)
        => Ok(await dispatcher.Query(new ListCrmClientArtifactsQuery(clientId, 200), ct));

    [HttpGet("projects/{projectId:guid}/clients/{clientId:guid}/chain-assignments")]
    public async Task<IActionResult> ListChainAssignments(
        Guid projectId,
        Guid clientId,
        CancellationToken ct)
        => Ok(await dispatcher.Query(
            new ListCrmClientAssignedJobChainsQuery(clientId, projectId),
            ct));

    [HttpPost("projects/{projectId:guid}/clients")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<IActionResult> SaveClient(
        Guid projectId,
        [FromBody] SaveCrmClientCommand request,
        CancellationToken ct)
    {
        if (request.ProjectId != projectId)
            return BadRequest(new { error = "Project id does not match the route." });
        return Ok(await dispatcher.Send(request, ct));
    }

    [HttpPut("clients/{clientId:guid}/stage")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<IActionResult> MoveClient(
        Guid clientId,
        [FromBody] MoveCrmClientRequest request,
        CancellationToken ct)
    {
        if (!Enum.TryParse<CustomerLifecycleStage>(request.LifecycleStage, true, out var stage))
            return BadRequest(new { error = "Unknown lifecycle stage." });
        return Ok(await dispatcher.Send(new MoveCrmClientCommand(clientId, stage), ct));
    }

    [HttpDelete("clients/{clientId:guid}")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<IActionResult> DeleteClient(Guid clientId, CancellationToken ct)
        => await dispatcher.Send(new DeleteCrmClientCommand(clientId), ct)
            ? NoContent()
            : NotFound();

    [HttpPut("clients/{clientId:guid}/portal")]
    [Authorize(Policy = Permission.SettingsManage)]
    public async Task<IActionResult> ConfigurePortal(
        Guid clientId,
        [FromBody] ConfigureCrmClientPortalCommand request,
        CancellationToken ct)
    {
        if (request.ClientId != clientId)
            return BadRequest(new { error = "Client id does not match the route." });
        return Ok(await dispatcher.Send(request, ct));
    }

    [HttpPost("clients/{clientId:guid}/automation-runs")]
    [Authorize(Policy = Permission.ChainsManage)]
    public async Task<IActionResult> RunAutomation(
        Guid clientId,
        [FromBody] RunCrmAutomationRequest request,
        CancellationToken ct)
        => Ok(await dispatcher.Send(
            new RunCrmClientAutomationCommand(clientId, request.ChainId, null, null),
            ct));

    [HttpPut("clients/{clientId:guid}/chain-assignments")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<IActionResult> SetChainAssignments(
        Guid clientId,
        [FromBody] SetCrmChainAssignmentsRequest request,
        CancellationToken ct)
        => Ok(await dispatcher.Send(
            new SetCrmClientAssignedJobChainsCommand(
                request.ProjectId,
                clientId,
                request.ChainIds),
            ct));

    [HttpPost("clients/{clientId:guid}/communications")]
    [Authorize(Policy = Permission.CrmCommsSend)]
    public async Task<IActionResult> SendCommunication(
        Guid clientId,
        [FromBody] SendCrmCommunicationRequest request,
        CancellationToken ct)
    {
        if (request.Channel.Equals("Note", StringComparison.OrdinalIgnoreCase))
            return Ok(await dispatcher.Send(new AddCrmClientNoteCommand(clientId, request.Body), ct));
        if (!Enum.TryParse<CrmCommunicationChannel>(request.Channel, true, out var channel))
            return BadRequest(new { error = "Unknown communication channel." });
        return Ok(await dispatcher.Send(
            new SendCrmClientMessageCommand(clientId, channel, request.Subject, request.Body),
            ct));
    }

    [HttpPost("clients/{clientId:guid}/artifacts")]
    [Authorize(Policy = Permission.DataWrite)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> AttachArtifact(
        Guid clientId,
        IFormFile file,
        CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return Ok(await dispatcher.Send(
            new AttachCrmClientArtifactCommand(
                clientId,
                file.FileName,
                file.ContentType,
                buffer.ToArray()),
            ct));
    }

    [HttpDelete("clients/{clientId:guid}/artifacts/{artifactId:guid}")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<IActionResult> RemoveArtifact(
        Guid clientId,
        Guid artifactId,
        CancellationToken ct)
    {
        var artifacts = await dispatcher.Query(new ListCrmClientArtifactsQuery(clientId, 200), ct);
        if (artifacts.All(artifact => artifact.Id != artifactId))
            return NotFound();
        return await dispatcher.Send(new RemoveCrmClientArtifactCommand(artifactId), ct)
            ? NoContent()
            : NotFound();
    }

    [HttpPost("projects/{projectId:guid}/appointments")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<IActionResult> SaveAppointment(
        Guid projectId,
        [FromBody] CreateCrmAppointmentCommand request,
        CancellationToken ct)
    {
        if (request.ProjectId != projectId)
            return BadRequest(new { error = "Project id does not match the route." });
        return Ok(await dispatcher.Send(request, ct));
    }

    [HttpDelete("appointments/{appointmentId:guid}")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<IActionResult> DeleteAppointment(Guid appointmentId, CancellationToken ct)
        => await dispatcher.Send(new DeleteCrmAppointmentCommand(appointmentId), ct)
            ? NoContent()
            : NotFound();

    [HttpPost("projects/{projectId:guid}/calendars")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<IActionResult> SaveCalendar(
        Guid projectId,
        [FromBody] SaveCrmCalendarCommand request,
        CancellationToken ct)
    {
        if (request.ProjectId != projectId)
            return BadRequest(new { error = "Project id does not match the route." });
        return Ok(await dispatcher.Send(request, ct));
    }

    [HttpDelete("calendars/{calendarId:guid}")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<IActionResult> DeleteCalendar(Guid calendarId, CancellationToken ct)
        => await dispatcher.Send(new DeleteCrmCalendarCommand(calendarId), ct)
            ? NoContent()
            : NotFound();

    [HttpPost("projects/{projectId:guid}/automations")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<IActionResult> SaveAutomation(
        Guid projectId,
        [FromBody] SaveCrmAutomationRuleCommand request,
        CancellationToken ct)
    {
        if (request.ProjectId != projectId)
            return BadRequest(new { error = "Project id does not match the route." });
        return Ok(await dispatcher.Send(request, ct));
    }

    [HttpPut("automations/{ruleId:guid}/enabled")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<IActionResult> SetAutomationEnabled(
        Guid ruleId,
        [FromBody] bool enabled,
        CancellationToken ct)
        => Ok(await dispatcher.Send(new SetCrmAutomationEnabledCommand(ruleId, enabled), ct));

    [HttpDelete("automations/{ruleId:guid}")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<IActionResult> DeleteAutomation(Guid ruleId, CancellationToken ct)
        => await dispatcher.Send(new DeleteCrmAutomationRuleCommand(ruleId), ct)
            ? NoContent()
            : NotFound();
}
