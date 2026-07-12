using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host;

/// <summary>
/// Locality: every timestamp the portal shows converts into the WORKSPACE's timezone (the tenant's
/// IANA <c>TimeZoneId</c>, set from Settings or the <c>set_workspace_timezone</c> MCP tool) — not
/// the server's clock, which is UTC in-cluster and made <c>ToLocalTime()</c> lie to everyone.
/// </summary>
public static class WorkspaceTime
{
    public static DateTimeOffset ToWorkspaceTime(this DateTimeOffset d)
    {
        var tzId = CurrentTenant.Current?.TimeZoneId ?? "UTC";
        try { return TimeZoneInfo.ConvertTime(d, TimeZoneInfo.FindSystemTimeZoneById(tzId)); }
        catch { return d; }
    }
}
