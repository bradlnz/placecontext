using PlaceContext.Application.Ports;

namespace PlaceContext.Projects.Infrastructure.Persistence;

internal sealed class ProjectsDesignTimeTenant : ICurrentTenant
{
    public Guid TenantId => Guid.Empty;
    public string Slug => string.Empty;
    public string TimeZoneId => "UTC";
    public bool IsResolved => true;
}
