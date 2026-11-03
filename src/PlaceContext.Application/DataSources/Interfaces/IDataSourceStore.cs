namespace PlaceContext.Application.DataSources;

public interface IDataSourceStore
{
    Task<IReadOnlyList<DataSourceDefinition>> ListAsync(Guid projectId, CancellationToken ct);
    Task<DataSourceDefinition> GetAsync(Guid projectId, Guid sourceId, CancellationToken ct);
    Task<DataSourceDefinition> CreateAsync(DataSourceDefinition source, string connectionString, CancellationToken ct);
    Task<IReadOnlyList<SourceDocument>> ReadDocumentsAsync(Guid projectId, Guid sourceId, CancellationToken ct);
    Task CommitAsync(DataSourceDefinition source, IReadOnlyList<SourceDocument> documents, DateTimeOffset now, CancellationToken ct);
    Task<DataSourceDefinition> UpdateAsync(DataSourceDefinition source, CancellationToken ct);
    Task DeleteAsync(Guid projectId, Guid sourceId, CancellationToken ct);
}
