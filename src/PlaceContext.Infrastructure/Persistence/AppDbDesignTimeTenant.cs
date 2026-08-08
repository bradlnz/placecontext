using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Persistence;

internal sealed class AppDbDesignTimeTenant : ICurrentTenant
{
    public Guid TenantId => Guid.Empty;
    public string Slug => string.Empty;
    public string TimeZoneId => "UTC";
    public bool IsResolved => false;
}
