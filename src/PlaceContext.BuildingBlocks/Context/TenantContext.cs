namespace PlaceContext.Application.Ports;

public sealed record TenantContext(Guid Id, string Slug, string TimeZoneId);
