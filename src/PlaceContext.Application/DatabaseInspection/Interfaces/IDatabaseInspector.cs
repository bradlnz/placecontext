namespace PlaceContext.Application.DatabaseInspection;

public interface IDatabaseInspector
{
    Task<PlaceContext.Application.ProjectData.ProjectQueryResult> QueryAsync(DatabaseSelectStatement query, CancellationToken ct = default);
    Task<IReadOnlyList<DatabaseTable>> ListTablesAsync(CancellationToken ct = default);
    Task<DatabaseTablePage> ReadTableAsync(string table, int offset, CancellationToken ct = default);
}
