using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Host.Auth;
using PlaceContext.Settings.Integration;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Route("api/v1/settings/backup")]
[Authorize(Policy = Policies.DefaultAdmin)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class BackupSettingsController(ISettingsBackupClient backup) : ControllerBase
{
    [HttpPost("imports")]
    public async Task<ActionResult<JsonElement>> Import(
        [FromBody] JsonElement manifest,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await backup.ImportAsync(manifest, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
