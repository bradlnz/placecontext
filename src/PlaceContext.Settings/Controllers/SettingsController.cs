using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PlaceContext.Settings.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize]
public sealed class SettingsController : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status() => Ok(new { service = "settings", status = "ready" });
}
