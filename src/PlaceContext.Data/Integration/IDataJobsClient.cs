namespace PlaceContext.Data.Integration;

public interface IDataJobsClient
{
    Task<DataJobCatalog> GetCatalogAsync(Guid projectId, CancellationToken ct = default);
}
