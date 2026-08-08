using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed record TriggerOpenSearchSyncCommand(Guid ProjectId)
    : ICommand<OpenSearchSyncView>, IRequiresPermission
{
    public string RequiredPermission => Permission.SettingsManage;
}