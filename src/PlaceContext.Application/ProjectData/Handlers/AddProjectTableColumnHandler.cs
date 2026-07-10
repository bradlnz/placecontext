using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class AddProjectTableColumnHandler : ICommandHandler<AddProjectTableColumnCommand, bool>
{
    private readonly IProjectRepository _projects;
    private readonly IProjectDataStore _store;
    public AddProjectTableColumnHandler(IProjectRepository projects, IProjectDataStore store) { _projects = projects; _store = store; }

    public async Task<bool> HandleAsync(AddProjectTableColumnCommand c, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(_projects, c.ProjectId, ct);
        await _store.AddColumnAsync(c.ProjectId, c.TableName, c.Column, ct);
        return true;
    }
}
