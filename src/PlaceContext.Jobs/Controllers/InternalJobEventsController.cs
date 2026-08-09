using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;

namespace PlaceContext.Jobs.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = "ApiKey")]
[Route("api/jobs/internal/events")]
public sealed class InternalJobEventsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Emit(EventRequest request, CancellationToken ct)
    {
        await dispatcher.Send(new EmitEventCommand(request.EventType, request.ProjectId, request.Payload), ct);
        return Accepted();
    }

    public sealed record EventRequest(string EventType, Guid ProjectId, string Payload);
}
