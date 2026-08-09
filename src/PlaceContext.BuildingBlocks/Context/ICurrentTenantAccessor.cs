namespace PlaceContext.Application.Ports;

public interface ICurrentTenantAccessor
{
    void Set(TenantContext tenant);
    void Clear();
}
