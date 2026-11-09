using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;
namespace PlaceContext.Application.Features;

public sealed record DeleteSavedQueryCommand(Guid QueryId)
    : ICommand<bool>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
