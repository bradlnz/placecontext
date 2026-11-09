using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
namespace PlaceContext.Application.Features;

public sealed record SetTenantBrandingCommand(TenantBranding Branding) : ICommand<bool>, IRequiresPermission
{
    public string RequiredPermission => Permission.SettingsManage;
}
