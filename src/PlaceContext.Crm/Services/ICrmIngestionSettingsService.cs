using PlaceContext.Crm.Contracts.Ingestion;

namespace PlaceContext.Crm.Services;

public interface ICrmIngestionSettingsService
{
    Task<CrmIngestionSettingsView> GetAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<CrmIngestionSettingsView> SaveOriginAsync(
        Guid projectId,
        string origin,
        CancellationToken cancellationToken = default);

    Task<CrmIngestionTokenResult> RotateAsync(
        Guid projectId,
        string origin,
        CancellationToken cancellationToken = default);

    Task DisableAsync(Guid projectId, CancellationToken cancellationToken = default);
}
