using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>
/// The tenant the current request belongs to, resolved from the <c>{user}.placecontext.ai</c>
/// subdomain. All tenant-owned data is isolated by <see cref="TenantId"/> (row-level filter). Backed
/// by an ambient (AsyncLocal) value so it flows into the dispatcher's per-operation DI scopes.
/// </summary>
public interface ICurrentTenant
{
    Guid TenantId { get; }
    string Slug { get; }
    /// <summary>The tenant's IANA timezone (e.g. "America/New_York"); the clock itself stays UTC.</summary>
    string TimeZoneId { get; }
    bool IsResolved { get; }
}
