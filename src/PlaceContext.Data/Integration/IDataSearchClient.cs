using PlaceContext.Application.Ports;

namespace PlaceContext.Data.Integration;

public interface IDataSearchClient
{
    Task<IReadOnlyList<DataSearchIndexSummary>> ListIndicesAsync(Guid projectId, CancellationToken ct = default);
    Task<ProjectQueryResult> QueryAsync(Guid projectId, string sql, CancellationToken ct = default);
    Task ReplaceIndexAsync(
        Guid projectId,
        string indexName,
        IReadOnlyList<DataSearchMappingField> mappingFields,
        IReadOnlyList<string> columnNames,
        IReadOnlyList<IReadOnlyList<string?>> rows,
        IReadOnlyList<string> jsonColumnNames,
        CancellationToken ct = default);
}
