using PlaceContext.Application.Ports;

namespace PlaceContext.Agents.Infrastructure.Persistence;

internal sealed class AgentsDesignTimeTenant : ICurrentTenant
{
    public Guid TenantId => Guid.Empty;
    public string Slug => string.Empty;
    public string TimeZoneId => "UTC";
    public bool IsResolved => false;
}
