using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.AgentChat.Controllers;

[ApiController]
[Route("api/agent-chat")]
[Authorize(Policy = Permission.AgentsChat)]
[Produces("application/json")]
public sealed class AgentChatController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("projects/{projectId:guid}/config")]
    public async Task<IActionResult> GetConfig(Guid projectId, CancellationToken ct)
        => Ok(await dispatcher.Query(new GetAgentConfigQuery(projectId), ct));

    [HttpGet("projects/{projectId:guid}/sessions")]
    public async Task<IActionResult> ListSessions(Guid projectId, CancellationToken ct)
        => Ok(await dispatcher.Query(new ListAgentChatSessionsQuery(projectId), ct));

    [HttpGet("sessions/{sessionId:guid}")]
    public async Task<IActionResult> GetSession(Guid sessionId, CancellationToken ct)
        => await dispatcher.Query(new GetAgentChatSessionQuery(sessionId), ct) is { } session
            ? Ok(session)
            : NotFound();
}
