using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Jobs.Contracts.Management;
using PlaceContext.Jobs.Management;

namespace PlaceContext.Jobs.Controllers;

[ApiController]
[Route("api/v1/schedules")]
[Authorize(AuthenticationSchemes = "ApiKey", Policy = Permission.TriggersManage)]
[Produces("application/json")]
public sealed class ScheduleOperationsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("{id:guid}", Name = "GetScheduleById")]
    public async Task<ActionResult<ScheduleResponse>> Get(Guid id, CancellationToken ct)
    {
        var trigger = await dispatcher.Query(new GetTriggerByIdQuery(id), ct);
        return trigger is null ? NotFound() : Ok(ScheduleApiMapper.ToResponse(trigger));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ScheduleResponse>> Update(
        Guid id,
        [FromBody] UpdateScheduleRequest request,
        CancellationToken ct)
    {
        if (await dispatcher.Query(new GetTriggerByIdQuery(id), ct) is null)
            return NotFound();

        try
        {
            var trigger = await dispatcher.Send(new UpdateTriggerCommand(
                id, request.Name, request.CronExpression, request.EventName, request.Enabled), ct);
            return Ok(ScheduleApiMapper.ToResponse(trigger));
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

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => await dispatcher.Send(new DeleteTriggerCommand(id), ct) ? NoContent() : NotFound();
}
