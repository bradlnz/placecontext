namespace PlaceContext.Data.Integration;

public interface IDataProjectsClient
{
    Task<IReadOnlyList<DataProjectSummary>> ListAsync(CancellationToken ct = default);
}
