using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PlaceContext.Operations.Controllers;

[ApiController]
[Route("api/operations")]
[Authorize]
public sealed class OperationsController : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status() => Ok(new { service = "operations", status = "ready" });
}
