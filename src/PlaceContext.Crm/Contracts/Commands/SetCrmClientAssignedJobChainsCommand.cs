using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed record SetCrmClientAssignedJobChainsCommand(
    Guid ProjectId,
    Guid ClientId,
    IReadOnlyList<Guid> ChainIds) : ICommand<IReadOnlyList<Guid>>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
