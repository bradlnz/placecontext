using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed record MoveCrmClientCommand(
    Guid ClientId,
    CustomerLifecycleStage LifecycleStage) : ICommand<CrmClientView>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
