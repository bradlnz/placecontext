using PlaceContext.Application.Access;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Application.Features;

public sealed record ListDataSourcesQuery(Guid ProjectId) : IQuery<IReadOnlyList<DataSourceDefinition>>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataRead;
}
