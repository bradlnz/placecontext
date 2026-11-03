namespace PlaceContext.Application.DatabaseInspection;

public interface IDatabaseInspector
{
    Task<IReadOnlyList<DatabaseTable>> ListTablesAsync(CancellationToken ct = default);
    Task<DatabaseTablePage> ReadTableAsync(string table, int offset, CancellationToken ct = default);
}
