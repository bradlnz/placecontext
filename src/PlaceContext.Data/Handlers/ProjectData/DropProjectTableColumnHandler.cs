using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class DropProjectTableColumnHandler : ICommandHandler<DropProjectTableColumnCommand, bool>
{
    private readonly IProjectRepository _projects;
    private readonly IProjectDataStore _store;
    public DropProjectTableColumnHandler(IProjectRepository projects, IProjectDataStore store) { _projects = projects; _store = store; }

    public async Task<bool> HandleAsync(DropProjectTableColumnCommand c, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(_projects, c.ProjectId, ct);
        await _store.DropColumnAsync(c.ProjectId, c.TableName, c.ColumnName, ct);
        return true;
    }
}
