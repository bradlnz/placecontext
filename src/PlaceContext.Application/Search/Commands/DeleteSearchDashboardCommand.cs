using PlaceContext.Application.Access;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
namespace PlaceContext.Application.Features;

public sealed record DeleteSearchDashboardCommand(Guid DashboardId)
    : ICommand<bool>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
