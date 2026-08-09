namespace PlaceContext.Identity.OAuth;

public sealed record IdentityTenantContext(
    Guid TenantId,
    string TenantSlug,
    string TenantTimeZone,
    Guid UserId);
