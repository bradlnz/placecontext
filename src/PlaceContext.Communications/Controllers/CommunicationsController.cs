using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PlaceContext.Communications.Controllers;

[ApiController]
[Route("api/communications")]
[Authorize]
public sealed class CommunicationsController : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status() => Ok(new { service = "communications", status = "ready" });
}
