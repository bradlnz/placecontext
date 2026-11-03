using PlaceContext.Application.Access;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Application.Features;

public sealed record SearchProjectSourcesQuery(Guid ProjectId, string Query, int Take = 10)
    : IQuery<IReadOnlyList<DataSourceSearchResult>>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataRead;
}
