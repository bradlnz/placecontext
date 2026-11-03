using PlaceContext.Application.Cqrs;
using PlaceContext.Application.ProjectData;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class QueryProjectDataHandler(IProjectRepository projects, IProjectDataReader reader)
    : IQueryHandler<QueryProjectDataQuery, ProjectQueryResult>
{
    public async Task<ProjectQueryResult> HandleAsync(QueryProjectDataQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.Sql) || query.Sql.Length > 32768)
        {
            throw new ArgumentException("SQL must contain between 1 and 32768 characters.");
        }
        await ProjectDataGuard.EnsureExistsAsync(projects, query.ProjectId, ct);
        return await reader.QueryAsync(query.ProjectId, query.Sql, ct);
    }
}
