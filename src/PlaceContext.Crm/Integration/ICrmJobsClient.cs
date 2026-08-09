namespace PlaceContext.Crm.Integration;

public interface ICrmJobsClient
{
    Task<CrmJobsCatalog> GetCatalogAsync(Guid projectId, CancellationToken ct = default);

    Task<CrmJobChainRun> RunChainAsync(
        CrmRunJobChainRequest request,
        CancellationToken ct = default);

    Task<CrmJobChainRun?> GetRunAsync(Guid chainRunId, CancellationToken ct = default);
}
