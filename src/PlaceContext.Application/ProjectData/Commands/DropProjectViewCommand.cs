using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;
namespace PlaceContext.Application.Features;

/// <summary>Drops a view from the project's schema.</summary>
public sealed record DropProjectViewCommand(Guid ProjectId, string Name) : ICommand<bool>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
