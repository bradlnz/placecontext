using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Access;
using PlaceContext.Application.Workload.VirtualMachines;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers;

[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + AgentAuthenticationDefaults.SchemeName,
    Policy = Permission.JobsView)]
public sealed class VirtualMachineReplayController(IVirtualMachineReplayStore replay) : ControllerBase
{
    [HttpGet("/projects/{projectId:guid}/workflow-runs/{runId:guid}/qemu/{step:int}/frames/{sequence:int}")]
    public async Task<IActionResult> Image(Guid projectId, Guid runId, int step, int sequence)
    {
        var png = await replay.ReadImageAsync(projectId, runId, step, sequence, HttpContext.RequestAborted);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return png is null ? NotFound() : File(png, "image/png");
    }
}
