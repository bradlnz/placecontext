using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;
namespace PlaceContext.Application.Features;

public sealed record GetProjectRunCountsQuery(Guid ProjectId) : IQuery<IReadOnlyDictionary<string, int>>, IRequiresPermission
{
    public string RequiredPermission => Permission.JobsView;
}
