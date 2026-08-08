using System.Globalization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Controllers.Api.Records;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/secrets")]
[Authorize(
    AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme,
    Policy = Permission.SecretsManage)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ProjectSecretsController(IPlaceContextService placeContextService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProjectSecretResponse>>> List(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var secrets = await placeContextService.ListProjectSecretsAsync(projectId, cancellationToken);
        return Ok(secrets.Select(secret => Map(secret.Name, secret.CreatedAt)));
    }

    [HttpPost]
    public async Task<ActionResult<ProjectSecretResponse>> Add(
        Guid projectId,
        [FromBody] CreateProjectSecretRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (name.Length == 0)
            return BadRequest(new { error = "Name is required." });
        if (request.Value.Length == 0)
            return BadRequest(new { error = "Value is required." });

        try
        {
            var secret = await placeContextService.AddProjectSecretAsync(
                projectId,
                name,
                request.Value,
                cancellationToken);
            return Ok(Map(secret.Name, secret.CreatedAt));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [HttpDelete("{name}")]
    public async Task<IActionResult> Delete(
        Guid projectId,
        string name,
        CancellationToken cancellationToken)
    {
        var deleted = await placeContextService.DeleteProjectSecretAsync(
            projectId,
            name,
            cancellationToken);
        return deleted ? NoContent() : NotFound(new { error = "The secret does not exist." });
    }

    private static ProjectSecretResponse Map(string name, DateTimeOffset createdAt) =>
        new(
            name,
            createdAt,
            createdAt.ToWorkspaceTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
}
