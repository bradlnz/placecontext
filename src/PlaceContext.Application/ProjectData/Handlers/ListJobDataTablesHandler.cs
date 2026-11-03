using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Jobs;
using PlaceContext.Application.ProjectData;

namespace PlaceContext.Application.Features;

public sealed class ListJobDataTablesHandler(
    IJobDataAccess access, IQueryHandler<ListProjectDataTablesQuery, IReadOnlyList<ProjectTableInfo>> reader)
    : IQueryHandler<ListJobDataTablesQuery, IReadOnlyList<ProjectTableInfo>>
{
    public Task<IReadOnlyList<ProjectTableInfo>> HandleAsync(ListJobDataTablesQuery query, CancellationToken ct = default)
    {
        return reader.HandleAsync(new ListProjectDataTablesQuery(access.RequireProjectId()), ct);
    }
}
