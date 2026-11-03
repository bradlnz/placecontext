using PlaceContext.Application.Access;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.ProjectData;

namespace PlaceContext.Application.Features;

public sealed record QueryProjectDataQuery(Guid ProjectId, string Sql) : IQuery<ProjectQueryResult>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataRead;
}
