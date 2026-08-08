using PlaceContext.Application.Ports;

namespace PlaceContext.Vault.Infrastructure.Persistence;

internal sealed class VaultDesignTimeTenant : ICurrentTenant
{
    public Guid TenantId => Guid.Empty;
    public string Slug => string.Empty;
    public string TimeZoneId => "UTC";
    public bool IsResolved => false;
}
