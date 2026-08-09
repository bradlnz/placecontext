namespace PlaceContext.Settings.Persistence;

public interface ISettingsStore
{
    Task<string?> GetBrandingAsync(Guid tenantId, CancellationToken ct = default);
    Task SetBrandingAsync(Guid tenantId, string? json, CancellationToken ct = default);
    Task<string?> GetMenuAsync(Guid tenantId, CancellationToken ct = default);
    Task SetMenuAsync(Guid tenantId, string json, CancellationToken ct = default);
    Task<string?> GetArtifactViewAsync(Guid tenantId, CancellationToken ct = default);
    Task SetArtifactViewAsync(Guid tenantId, string json, CancellationToken ct = default);
    Task SetTimeZoneAsync(Guid tenantId, string timeZoneId, CancellationToken ct = default);
    Task<bool> IsDefaultAdminAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}
