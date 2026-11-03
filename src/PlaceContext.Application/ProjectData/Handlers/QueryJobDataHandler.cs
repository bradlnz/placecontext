using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Jobs;
using PlaceContext.Application.ProjectData;

namespace PlaceContext.Application.Features;

public sealed class QueryJobDataHandler(
    IJobDataAccess access, IQueryHandler<QueryProjectDataQuery, ProjectQueryResult> reader)
    : IQueryHandler<QueryJobDataQuery, ProjectQueryResult>
{
    public Task<ProjectQueryResult> HandleAsync(QueryJobDataQuery query, CancellationToken ct = default)
    {
        // The run credential replaces user permission checks and fixes the project before shared query validation.
        return reader.HandleAsync(new QueryProjectDataQuery(access.RequireProjectId(), query.Sql), ct);
    }
}
