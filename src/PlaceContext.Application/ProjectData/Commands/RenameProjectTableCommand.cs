using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>Rename a table in a project's database.</summary>
public sealed record RenameProjectTableCommand(Guid ProjectId, string From, string To) : ICommand<bool>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
