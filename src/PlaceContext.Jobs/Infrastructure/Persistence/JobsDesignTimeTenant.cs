using PlaceContext.Application.Ports;

namespace PlaceContext.Jobs.Infrastructure.Persistence;

internal sealed class JobsDesignTimeTenant : ICurrentTenant
{
    public Guid TenantId => Guid.Empty;
    public string Slug => string.Empty;
    public string TimeZoneId => "UTC";
    public bool IsResolved => false;
}
