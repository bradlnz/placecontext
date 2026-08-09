using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Operations.Contracts.Backup;

/// <summary>Tenant registry settings safe to carry across a backup (see <c>ITenantSettingsPort</c>).</summary>
public sealed record TenantSettingsManifest(string TimeZoneId, string? BrandingJson, string? GitHubLogin);
