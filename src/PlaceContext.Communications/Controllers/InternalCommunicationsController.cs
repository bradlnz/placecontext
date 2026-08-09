using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Communications.Contracts;

namespace PlaceContext.Communications.Controllers;

[ApiController]
[Route("api/communications/internal")]
[Authorize(AuthenticationSchemes = "ApiKey")]
public sealed class InternalCommunicationsController(ICommunicationSender sender) : ControllerBase
{
    [HttpGet("two-factor-channels")]
    public async Task<IActionResult> TwoFactorChannels(
        [FromServices] ICommunicationProviderService providers,
        CancellationToken ct)
        => Ok(await providers.TwoFactorChannelsAsync(ct));

    [HttpGet("capabilities")]
    public async Task<IActionResult> Capabilities(CancellationToken ct)
        => Ok(await sender.GetCapabilitiesAsync(ct));

    [HttpPost("email")]
    public async Task<IActionResult> SendEmail(
        SendCommunicationEmailRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await sender.SendEmailAsync(request, ct));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPost("sms")]
    public async Task<IActionResult> SendSms(
        SendCommunicationSmsRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await sender.SendSmsAsync(request, ct));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
