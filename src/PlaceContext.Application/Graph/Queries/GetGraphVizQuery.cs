using PlaceContext.Application.Access;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

public sealed record GetGraphVizQuery(Guid ProjectId) : IQuery<GraphVizView>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataRead;
}
