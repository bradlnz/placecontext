using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
namespace PlaceContext.Application.Features;

public sealed record TriggerSearchSyncCommand(Guid ProjectId)
    : ICommand<SearchSyncView>, IRequiresPermission
{
    public string RequiredPermission => Permission.SettingsManage;
}