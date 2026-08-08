using PlaceContext.Application.Ports;

namespace PlaceContext.Data.Infrastructure.Persistence;

internal sealed class DataDesignTimeTenant : ICurrentTenant
{
    public Guid TenantId => Guid.Empty;
    public string Slug => string.Empty;
    public string TimeZoneId => "UTC";
    public bool IsResolved => false;
}
