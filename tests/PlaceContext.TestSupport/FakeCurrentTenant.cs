using PlaceContext.Application.Ports;

namespace PlaceContext.TestSupport;

/// <summary>A fixed tenant for tests.</summary>
public sealed class FakeCurrentTenant : ICurrentTenant
{
    public FakeCurrentTenant(Guid tenantId, string slug = "test", string timeZoneId = "UTC")
    {
        TenantId = tenantId;
        Slug = slug;
        TimeZoneId = timeZoneId;
    }

    public Guid TenantId { get; }
    public string Slug { get; }
    public string TimeZoneId { get; }
    public bool IsResolved => true;
}
