using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.SavedQueries;
namespace PlaceContext.Application.Features;

public sealed record SaveSavedQueryCommand(
    Guid ProjectId,
    string Name,
    string Sql) : ICommand<SavedQueryRecord>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
