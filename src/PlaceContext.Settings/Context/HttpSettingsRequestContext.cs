using Microsoft.AspNetCore.Http;

namespace PlaceContext.Settings.Context;

public sealed class HttpSettingsRequestContext(IHttpContextAccessor accessor) : ISettingsRequestContext
{
    private string? TenantClaim => accessor.HttpContext?.User.FindFirst("tenant")?.Value;

    public Guid TenantId => Guid.TryParse(TenantClaim, out var tenantId) ? tenantId : Guid.Empty;
    public string TimeZoneId => accessor.HttpContext?.User.FindFirst("tenant_timezone")?.Value ?? "UTC";
    public bool IsResolved => TenantId != Guid.Empty;
}
