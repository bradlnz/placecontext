using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers;

[Authorize(Policy = Permission.SettingsManage)]
public sealed class ServiceAppsController(ServiceAppSso sso, IServiceConsole services, ICurrentTenant tenant, ICurrentUser user) : ControllerBase
{
    [HttpGet("/services/open/{project:guid}/{service:guid}")]
    public async Task<IActionResult> Open(Guid project, Guid service, CancellationToken ct)
    {
        await services.GetSsoEndpointAsync(project, service, ct);
        return Redirect(sso.AppOrigin(project, service).AbsoluteUri);
    }

    [HttpGet("/services/sso/{project:guid}/{service:guid}")]
    public async Task<IActionResult> SignIn(Guid project, Guid service, string? state, string? returnPath, CancellationToken ct)
    {
        if (state?.Length != 64 || !state.All(Uri.IsHexDigit) || Request.Host != new HostString(sso.PortalOrigin.Authority)) return BadRequest();
        await services.GetSsoEndpointAsync(project, service, ct);
        var session = new ServiceAppSession(tenant.TenantId, user.UserId, user.Role, project, service, DateTimeOffset.UtcNow.AddMinutes(15));
        var code = sso.Issue(session, state, returnPath ?? "/");
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return Redirect(new Uri(sso.AppOrigin(project, service), "/__pc/sso?code=" + code).AbsoluteUri);
    }
}
