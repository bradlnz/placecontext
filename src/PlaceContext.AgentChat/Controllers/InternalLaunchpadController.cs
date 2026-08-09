using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;

namespace PlaceContext.AgentChat.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = "ApiKey")]
[Route("api/agent-chat/internal/launchpads")]
public sealed class InternalLaunchpadController(ILaunchpadRunner runner) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Run(LaunchpadRequest request, CancellationToken ct)
        => Ok(new { id = await runner.RunLaunchpadAsync(
            request.ProjectId, request.TriggerName, request.Prompt,
            request.SourceTable, request.ChainId, ct) });

    public sealed record LaunchpadRequest(
        Guid ProjectId,
        string TriggerName,
        string Prompt,
        string? SourceTable,
        Guid ChainId);
}
