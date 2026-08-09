using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Mcp.Contracts.Api;

namespace PlaceContext.Mcp.Controllers;

[ApiController]
[Route("api/mcp")]
public sealed class McpController : ControllerBase
{
    [HttpGet("status")]
    [AllowAnonymous]
    public ActionResult<McpServiceStatus> Status()
        => Ok(new McpServiceStatus("mcp", "ready"));
}
