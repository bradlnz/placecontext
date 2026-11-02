using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed record GetProjectRunCountsQuery(Guid ProjectId) : IQuery<IReadOnlyDictionary<string, int>>, IRequiresPermission
{
    public string RequiredPermission => Permission.JobsView;
}
