namespace PlaceContext.Application.DataSources;

public interface IDataSourceDiscovery
{
    Task<IReadOnlyList<SourceDataset>> DiscoverAsync(string provider, string connection, CancellationToken ct);
}
