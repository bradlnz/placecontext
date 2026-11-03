using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.DatabaseInspection;

public sealed class ReadDatabaseTableHandler(DatabaseInspectionAccess access, IDatabaseInspector inspector)
    : IQueryHandler<ReadDatabaseTableQuery, DatabaseTablePage>
{
    public async Task<DatabaseTablePage> HandleAsync(ReadDatabaseTableQuery query, CancellationToken ct = default)
    {
        await access.EnsureAsync(ct);
        if (string.IsNullOrWhiteSpace(query.Table) || query.Offset < 0)
        {
            throw new ArgumentException("Choose a table and a non-negative page offset.");
        }
        return await inspector.ReadTableAsync(query.Table, query.Offset, ct);
    }
}
