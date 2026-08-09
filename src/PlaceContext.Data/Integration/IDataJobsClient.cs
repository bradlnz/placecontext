using System.Text.Json;

namespace PlaceContext.Data.Integration;

public interface IDataJobsClient
{
    Task<DataJobCatalog> GetCatalogAsync(Guid projectId, CancellationToken ct = default);
    Task<JsonElement> RunAsync(
        Guid jobId,
        DataJobRunRequest request,
        CancellationToken ct = default);
}
