using PlaceContext.Application.Access;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Jobs;

namespace PlaceContext.Application.Features;

public sealed record ListSourceQueuesQuery(Guid ProjectId) : IQuery<IReadOnlyList<ChainQueueDefinition>>, IRequiresPermission
{
    public string RequiredPermission => Permission.ChainsManage;
}
