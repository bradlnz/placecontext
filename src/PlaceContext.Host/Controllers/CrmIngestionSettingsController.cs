using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Infrastructure.Crm;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Route("api/crm/ingestion/settings")]
[Authorize(Policy = Permission.SettingsManage)]
public sealed class CrmIngestionSettingsController : ControllerBase
{
    private readonly CrmIngestionSettingsService _settings;

    public CrmIngestionSettingsController(CrmIngestionSettingsService settings)
        => _settings = settings;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid projectId, CancellationToken ct)
        => Ok(await _settings.GetAsync(projectId, ct));

    [HttpPut]
    public async Task<IActionResult> Save(
        [FromBody] SaveCrmIngestionSettingsRequest request,
        CancellationToken ct)
    {
        try { return Ok(await _settings.SaveOriginAsync(request.ProjectId, request.AllowedOrigin, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpPost("rotate")]
    public async Task<IActionResult> Rotate(
        [FromBody] SaveCrmIngestionSettingsRequest request,
        CancellationToken ct)
    {
        try { return Ok(await _settings.RotateAsync(request.ProjectId, request.AllowedOrigin, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpDelete]
    public async Task<IActionResult> Disable([FromQuery] Guid projectId, CancellationToken ct)
    {
        await _settings.DisableAsync(projectId, ct);
        return NoContent();
    }

}
