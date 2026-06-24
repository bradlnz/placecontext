using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Tenancy;

/// <summary>The resolved tenant for the current request.</summary>
public sealed record TenantInfo(Guid Id, string Slug, string Name, string TimeZoneId);
