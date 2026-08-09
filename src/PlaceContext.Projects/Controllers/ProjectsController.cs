using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PlaceContext.Projects.Controllers;

[ApiController]
[Route("api/projects")]
[Authorize]
public sealed class ProjectsController : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status() => Ok(new { service = "projects", status = "ready" });
}
