using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.DatabaseInspection;

public sealed class ListDatabaseTablesHandler(DatabaseInspectionAccess access, IDatabaseInspector inspector)
    : IQueryHandler<ListDatabaseTablesQuery, IReadOnlyList<DatabaseTable>>
{
    public async Task<IReadOnlyList<DatabaseTable>> HandleAsync(ListDatabaseTablesQuery query, CancellationToken ct = default)
    {
        await access.EnsureAsync(ct);
        return await inspector.ListTablesAsync(ct);
    }
}
