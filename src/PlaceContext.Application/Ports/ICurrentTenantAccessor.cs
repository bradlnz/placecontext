namespace PlaceContext.Application.Ports;

/// <summary>Sets and clears the tenant context for the current asynchronous request flow.</summary>
public interface ICurrentTenantAccessor
{
    void Set(TenantContext tenant);
    void Clear();
}
