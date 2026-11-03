namespace PlaceContext.Application.Tenancy;

public interface ITenantRuntime
{
    Task<string> EnsureAsync(Guid tenantId, CancellationToken ct = default);
}
