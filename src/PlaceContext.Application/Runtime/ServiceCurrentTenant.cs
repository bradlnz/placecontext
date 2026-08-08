using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Runtime;

/// <summary>Async-flow tenant context used by independently hosted services.</summary>
public sealed class ServiceCurrentTenant : ICurrentTenant, ICurrentTenantAccessor
{
    private readonly AsyncLocal<TenantContext?> _current = new();

    public Guid TenantId => _current.Value?.Id ?? Guid.Empty;
    public string Slug => _current.Value?.Slug ?? string.Empty;
    public string TimeZoneId => _current.Value?.TimeZoneId ?? "UTC";
    public bool IsResolved => _current.Value is not null;

    public void Set(TenantContext tenant) => _current.Value = tenant;

    public void Clear() => _current.Value = null;
}
