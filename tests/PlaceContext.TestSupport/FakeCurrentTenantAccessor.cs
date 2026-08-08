using PlaceContext.Application.Ports;

namespace PlaceContext.TestSupport;

public sealed class FakeCurrentTenantAccessor : ICurrentTenantAccessor
{
    private readonly AsyncLocal<TenantContext?> _current = new();

    public TenantContext? Current => _current.Value;
    public void Set(TenantContext tenant) => _current.Value = tenant;
    public void Clear() => _current.Value = null;
}
