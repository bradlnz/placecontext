using PlaceContext.Application.Ports;

namespace PlaceContext.Search.Infrastructure.Persistence;

internal sealed class SearchDesignTimeTenant : ICurrentTenant
{
    public Guid TenantId => Guid.Empty;
    public string Slug => string.Empty;
    public string TimeZoneId => "UTC";
    public bool IsResolved => false;
}
