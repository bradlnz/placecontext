using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed record ListSavedQueriesQuery(Guid ProjectId)
    : IQuery<IReadOnlyList<SavedQueryRecord>>;

public sealed record SaveSavedQueryCommand(
    Guid ProjectId,
    string Name,
    string Sql) : ICommand<SavedQueryRecord>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}

public sealed record DeleteSavedQueryCommand(Guid QueryId)
    : ICommand<bool>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
