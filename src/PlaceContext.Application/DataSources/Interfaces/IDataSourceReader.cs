namespace PlaceContext.Application.DataSources;

public interface IDataSourceReader
{
    Task<IReadOnlyList<SourceRecord>> ReadAsync(DataSourceDefinition source, CancellationToken ct);
}
