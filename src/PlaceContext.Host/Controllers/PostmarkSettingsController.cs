using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Comms;

namespace PlaceContext.Host.Controllers;

[Authorize(Policy = Permission.SettingsManage)]
[Authorize(Policy = Permission.SecretsManage)]
public sealed class PostmarkSettingsController : ControllerBase
{
    private readonly PostmarkConnectionService _postmark;

    public PostmarkSettingsController(PostmarkConnectionService postmark) => _postmark = postmark;

    [HttpGet("/api/settings/postmark")]
    public async Task<IActionResult> Status(CancellationToken ct)
        => Ok(await _postmark.GetStatusAsync(ct));

    [HttpPost("/api/settings/postmark")]
    public async Task<IActionResult> Save(
        [FromBody] SavePostmarkSettingsRequest request,
        CancellationToken ct)
    {
        try
        {
            await _postmark.SaveSettingsAsync(
                request.VaultProjectId,
                request.ServerTokenSecretName,
                request.FromEmail,
                request.FromName,
                request.MessageStream,
                ct);
            return Ok(await _postmark.GetStatusAsync(ct));
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpDelete("/api/settings/postmark")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        await _postmark.DisconnectAsync(ct);
        return NoContent();
    }

    public sealed record SavePostmarkSettingsRequest(
        Guid VaultProjectId,
        string ServerTokenSecretName,
        string FromEmail,
        string FromName,
        string MessageStream);
}
