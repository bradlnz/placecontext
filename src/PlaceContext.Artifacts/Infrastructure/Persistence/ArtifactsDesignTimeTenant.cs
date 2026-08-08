using PlaceContext.Application.Ports;

namespace PlaceContext.Artifacts.Infrastructure.Persistence;

internal sealed class ArtifactsDesignTimeTenant : ICurrentTenant
{
    public Guid TenantId => Guid.Empty;
    public string Slug => string.Empty;
    public string TimeZoneId => "UTC";
    public bool IsResolved => false;
}
