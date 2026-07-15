using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// Liveness/readiness probe. Must stay a cookieless 200 — auto-login turning "/" into a redirect
/// dance made kubelet treat the pod as failing after 10 hops (see <see cref="AuthController"/>).
/// </summary>
[AllowAnonymous]
[Route("/healthz")]
public sealed class HealthController : ControllerBase
{
    // Explicit JsonResult (not the bare string) so the wire format matches the old minimal-API
    // `Results.Ok("ok")`, which JSON-encodes the string as `"ok"` — a plain MVC `Ok("ok")` would
    // instead hit the StringOutputFormatter and write unquoted text/plain.
    [HttpGet]
    public IActionResult Get() => new JsonResult("ok");
}
