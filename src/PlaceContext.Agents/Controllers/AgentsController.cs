using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Agents.Contracts.Commands;
using PlaceContext.Agents.Contracts.Queries;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Agents.Controllers;

[ApiController]
[Route("api/v1/agents")]
[Authorize(Policy = Permission.AgentsManage)]
[Produces("application/json")]
public sealed class AgentsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("workspace")]
    public async Task<IActionResult> Workspace([FromQuery] Guid? projectId, CancellationToken ct)
        => Ok(await dispatcher.Query(new GetAgentsWorkspaceQuery(projectId), ct));

    [HttpPost("profiles")]
    public async Task<IActionResult> CreateProfile([FromBody] CreateAgentProfileCommand command, CancellationToken ct)
    {
        var created = await dispatcher.Send(command, ct);
        return CreatedAtAction(nameof(Workspace), new { }, created);
    }

    [HttpPut("profiles/{id:guid}")]
    public async Task<IActionResult> UpdateProfile(Guid id, [FromBody] UpdateAgentProfileCommand command, CancellationToken ct)
        => await dispatcher.Send(command with { Id = id }, ct) is { } updated ? Ok(updated) : NotFound();

    [HttpPost("staff")]
    public async Task<IActionResult> CreateStaff([FromBody] CreateStaffMemberCommand command, CancellationToken ct)
    {
        var created = await dispatcher.Send(command, ct);
        return CreatedAtAction(nameof(Workspace), new { }, created);
    }

    [HttpPut("staff/{id:guid}/status")]
    public async Task<IActionResult> SetStaffStatus(Guid id, [FromBody] SetStaffStatusCommand command, CancellationToken ct)
        => await dispatcher.Send(command with { Id = id }, ct) is { } updated ? Ok(updated) : NotFound();

    [HttpPost("assignments")]
    public async Task<IActionResult> CreateAssignment([FromBody] CreateAgentAssignmentCommand command, CancellationToken ct)
    {
        var created = await dispatcher.Send(command, ct);
        return CreatedAtAction(nameof(Workspace), new { projectId = created.ProjectId }, created);
    }

    [HttpPost("approvals/{id:guid}/decision")]
    public async Task<IActionResult> ResolveApproval(Guid id, [FromBody] ResolveAgentApprovalCommand command, CancellationToken ct)
        => await dispatcher.Send(command with { Id = id }, ct) is { } updated ? Ok(updated) : NotFound();
}
