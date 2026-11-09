using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;
namespace PlaceContext.Application.Features;

public sealed record DeleteSearchDashboardCommand(Guid DashboardId)
    : ICommand<bool>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
