using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Tenancy;

/// <summary>
/// Ambient (AsyncLocal) holder for the current tenant. Registered as a singleton but reads a value
/// that flows with the async execution context — so it is correct inside the dispatcher's per-operation
/// child DI scopes (a plain scoped service would be a fresh, unresolved instance there). The tenant
/// resolution middleware calls <see cref="Set"/> at the start of each request.
/// </summary>
public sealed class CurrentTenant : ICurrentTenant
{
    private static readonly AsyncLocal<TenantInfo?> _current = new();

    /// <summary>The tenant captured in the current async flow (used by the Blazor circuit handler to re-apply it).</summary>
    public static TenantInfo? Current => _current.Value;

    public static void Set(TenantInfo tenant) => _current.Value = tenant;
    public static void Clear() => _current.Value = null;

    public Guid TenantId => _current.Value?.Id ?? Guid.Empty;
    public string Slug => _current.Value?.Slug ?? string.Empty;
    public string TimeZoneId => _current.Value?.TimeZoneId ?? "UTC";
    public bool IsResolved => _current.Value is not null;
}
