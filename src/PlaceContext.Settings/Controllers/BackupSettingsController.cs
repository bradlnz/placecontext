using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Route("api/v1/settings/backup")]
[Authorize(Policy = Policies.DefaultAdmin)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class BackupSettingsController(IPlaceContextService placeContextService) : ControllerBase
{
    [HttpPost("imports")]
    public async Task<ActionResult<ImportResultView>> Import(
        [FromBody] BackupManifest manifest,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await placeContextService.ImportManifestAsync(
                manifest,
                ct: cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
