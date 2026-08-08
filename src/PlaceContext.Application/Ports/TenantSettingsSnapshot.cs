namespace PlaceContext.Application.Ports;

/// <summary>Portable tenant settings — see <see cref="ITenantSettingsPort"/> for what's excluded and why.</summary>
public sealed record TenantSettingsSnapshot(string TimeZoneId, string? BrandingJson, string? GitHubLogin);
