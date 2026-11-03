using PlaceContext.Application.Access;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Application.Features;

public sealed record SearchDataSourceQuery(Guid ProjectId, Guid SourceId, string Query, int Take = 10) : IQuery<DataSourceSearchResult>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataRead;
}
