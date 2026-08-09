namespace PlaceContext.Crm.Integration;

public interface ICrmProjectsClient
{
    Task<IReadOnlyList<CrmProjectSummary>> ListAsync(CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid projectId, CancellationToken ct = default);
}
