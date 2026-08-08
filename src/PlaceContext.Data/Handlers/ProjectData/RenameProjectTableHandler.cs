using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class RenameProjectTableHandler : ICommandHandler<RenameProjectTableCommand, bool>
{
    private readonly IProjectRepository _projects;
    private readonly IProjectDataStore _store;
    public RenameProjectTableHandler(IProjectRepository projects, IProjectDataStore store) { _projects = projects; _store = store; }

    public async Task<bool> HandleAsync(RenameProjectTableCommand c, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(_projects, c.ProjectId, ct);
        await _store.RenameTableAsync(c.ProjectId, c.From, c.To, ct);
        return true;
    }
}
