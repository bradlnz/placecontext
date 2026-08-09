using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed record SaveSavedQueryCommand(
    Guid ProjectId,
    string Name,
    string Sql) : ICommand<SavedQueryRecord>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
