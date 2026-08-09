using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.AgentChat.Contracts.Api;

namespace PlaceContext.AgentChat.Controllers;

[ApiController]
[Route("api/agent-chat")]
[Authorize(Policy = Permission.AgentsChat)]
[Produces("application/json")]
public sealed class AgentChatController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("projects/{projectId:guid}/page")]
    public async Task<ActionResult<ChatPageResponse>> GetPage(Guid projectId, CancellationToken ct)
    {
        var configTask = dispatcher.Query(new GetAgentConfigQuery(projectId), ct);
        var sessionsTask = dispatcher.Query(new ListAgentChatSessionsQuery(projectId), ct);
        await Task.WhenAll(configTask, sessionsTask);
        return Ok(new ChatPageResponse(await configTask, await sessionsTask));
    }

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

    [HttpPost("projects/{projectId:guid}/messages")]
    public async Task<IActionResult> SendMessage(
        Guid projectId,
        SendChatMessageRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { error = "A message is required." });

        if (request.SessionId is { } sessionId)
        {
            var session = await dispatcher.Query(new GetAgentChatSessionQuery(sessionId), ct);
            if (session is null || session.ProjectId != projectId)
                return NotFound(new { error = "Chat session not found." });
        }

        try
        {
            return Ok(await dispatcher.Send(
                new SendAgentMessageCommand(projectId, request.SessionId, request.Message.Trim()),
                ct));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPut("projects/{projectId:guid}/settings")]
    public async Task<IActionResult> UpdateSettings(
        Guid projectId,
        UpdateChatSettingsRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BaseModel))
            return BadRequest(new { error = "A base model is required." });
        if (request.MaxContextChunks < 1)
            return BadRequest(new { error = "Maximum context chunks must be at least one." });

        try
        {
            return Ok(await dispatcher.Send(
                new UpdateAgentConfigCommand(
                    projectId,
                    request.BaseModel,
                    request.SystemPrompt ?? string.Empty,
                    request.Preamble ?? string.Empty,
                    request.ToolCatalog ?? string.Empty,
                    request.LaunchpadToolCatalog ?? string.Empty,
                    request.MaxContextChunks,
                    request.Temperature,
                    request.TopP,
                    request.Enabled),
                ct));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
