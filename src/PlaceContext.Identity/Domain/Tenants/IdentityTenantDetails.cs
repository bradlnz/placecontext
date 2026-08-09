namespace PlaceContext.Identity.Domain.Tenants;

public sealed record IdentityTenantDetails(
    Guid Id,
    string Slug,
    string Name,
    string TimeZoneId,
    string? CustomerPortalDomain,
    bool CustomerPortalEnabled,
    string? GitHubLogin,
    string? GitHubToken);
