using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Vault.Contracts.Api;

namespace PlaceContext.Vault.Controllers;

[ApiController]
[Route("api/vault")]
[Authorize(Policy = Permission.SecretsManage)]
[Produces("application/json")]
public sealed class VaultController(IDispatcher dispatcher, ICurrentTenant currentTenant) : ControllerBase
{
    [HttpGet("projects/{projectId:guid}/secrets")]
    public async Task<IActionResult> List(Guid projectId, CancellationToken ct)
        => Ok((await dispatcher.Query(new ListProjectSecretsQuery(projectId), ct)).Select(Map));

    [HttpPost("projects/{projectId:guid}/secrets")]
    public async Task<IActionResult> Add(
        Guid projectId,
        CreateProjectSecretRequest request,
        CancellationToken ct)
    {
        var name = request.Name.Trim();
        if (name.Length == 0)
            return BadRequest(new { error = "Name is required." });
        if (request.Value.Length == 0)
            return BadRequest(new { error = "Value is required." });

        try
        {
            return Ok(Map(await dispatcher.Send(
                new AddProjectSecretCommand(projectId, name, request.Value), ct)));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [HttpDelete("projects/{projectId:guid}/secrets/{name}")]
    public async Task<IActionResult> Delete(Guid projectId, string name, CancellationToken ct)
        => await dispatcher.Send(new DeleteProjectSecretCommand(projectId, name), ct)
            ? NoContent()
            : NotFound();

    private ProjectSecretResponse Map(PlaceContext.Application.Dtos.ProjectSecretView secret)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(currentTenant.TimeZoneId);
        var local = TimeZoneInfo.ConvertTime(secret.CreatedAt, timeZone);
        return new ProjectSecretResponse(
            secret.Name,
            secret.CreatedAt,
            local.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));
    }
}
