using PlaceContext.Application.Ports;

namespace PlaceContext.Identity.Infrastructure.Persistence;

public sealed class IdentityDesignTimeTenant : ICurrentTenant
{
    public Guid TenantId => Guid.Empty;
    public string Slug => "design-time";
    public string TimeZoneId => "UTC";
    public bool IsResolved => true;
}
