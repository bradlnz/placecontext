using PlaceContext.Application.Cqrs;
using PlaceContext.Application.ProjectData;

namespace PlaceContext.Application.DatabaseInspection;

public sealed class QueryDatabaseHandler(DatabaseInspectionAccess access, IDatabaseInspector inspector)
    : IQueryHandler<QueryDatabaseQuery, ProjectQueryResult>
{
    public async Task<ProjectQueryResult> HandleAsync(QueryDatabaseQuery query, CancellationToken ct = default)
    {
        await access.EnsureAsync(ct);
        return await inspector.QueryAsync(DatabaseSelectParser.Parse(query.Sql), ct);
    }
}
