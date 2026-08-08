using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed record SaveCrmClientCommand(
    Guid ProjectId,
    string Name,
    string? Company,
    string? Email,
    string? Phone,
    CustomerLifecycleStage LifecycleStage,
    string? Notes,
    Guid? ClientId = null) : ICommand<CrmClientView>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
