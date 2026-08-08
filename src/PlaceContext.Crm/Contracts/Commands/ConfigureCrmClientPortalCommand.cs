using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed record ConfigureCrmClientPortalCommand(
    Guid ClientId,
    bool Enabled,
    string? Slug,
    string? Domain,
    string? PortalBrandName = null,
    string? PortalBrandLogoUrl = null,
    string? DefaultPortalUserName = null,
    string? DefaultPortalUserEmail = null,
    string? DefaultPortalUserPassword = null) : ICommand<CrmClientView>, IRequiresPermission
{
    public string RequiredPermission => Permission.SettingsManage;
}
