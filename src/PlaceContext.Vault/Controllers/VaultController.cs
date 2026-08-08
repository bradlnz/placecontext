using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Vault.Controllers;

[ApiController]
[Route("api/vault")]
[Authorize(Policy = Permission.SecretsManage)]
[Produces("application/json")]
public sealed class VaultController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("projects/{projectId:guid}/secrets")]
    public async Task<IActionResult> List(Guid projectId, CancellationToken ct)
        => Ok(await dispatcher.Query(new ListProjectSecretsQuery(projectId), ct));

    [HttpPost("secrets")]
    public async Task<IActionResult> Add([FromBody] AddProjectSecretCommand command, CancellationToken ct)
        => Ok(await dispatcher.Send(command, ct));

    [HttpDelete("projects/{projectId:guid}/secrets/{name}")]
    public async Task<IActionResult> Delete(Guid projectId, string name, CancellationToken ct)
        => await dispatcher.Send(new DeleteProjectSecretCommand(projectId, name), ct)
            ? NoContent()
            : NotFound();
}
