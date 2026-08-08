namespace PlaceContext.Application.Ports;

/// <summary>Trusted tenant identity propagated by an authenticated service request.</summary>
public sealed record TenantContext(Guid Id, string Slug, string TimeZoneId);
