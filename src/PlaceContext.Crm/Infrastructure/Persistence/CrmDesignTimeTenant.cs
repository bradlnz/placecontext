using PlaceContext.Application.Ports;

namespace PlaceContext.Crm.Infrastructure.Persistence;

internal sealed class CrmDesignTimeTenant : ICurrentTenant
{
    public Guid TenantId => Guid.Empty;
    public string Slug => string.Empty;
    public string TimeZoneId => "UTC";
    public bool IsResolved => false;
}
