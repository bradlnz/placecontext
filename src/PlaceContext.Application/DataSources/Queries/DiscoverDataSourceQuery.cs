using PlaceContext.Application.Access;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Application.Features;

public sealed record DiscoverDataSourceQuery(Guid ProjectId, string Provider, string Connection) : IQuery<IReadOnlyList<SourceDataset>>, IRequiresPermission
{
    public string RequiredPermission => Permission.SecretsManage;
}
