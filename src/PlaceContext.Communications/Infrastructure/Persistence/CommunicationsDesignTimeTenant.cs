using PlaceContext.Application.Ports;

namespace PlaceContext.Communications.Infrastructure.Persistence;

internal sealed class CommunicationsDesignTimeTenant : ICurrentTenant
{
    public Guid TenantId => Guid.Empty;
    public string Slug => "design-time";
    public string TimeZoneId => "UTC";
    public bool IsResolved => true;
}
