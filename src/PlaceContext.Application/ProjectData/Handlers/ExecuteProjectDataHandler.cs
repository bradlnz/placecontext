using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class ExecuteProjectDataHandler : ICommandHandler<ExecuteProjectDataCommand, ProjectQueryResult>
{
    private readonly IProjectRepository _projects;
    private readonly IProjectDataStore _store;

    public ExecuteProjectDataHandler(IProjectRepository projects, IProjectDataStore store)
    {
        _projects = projects;
        _store = store;
    }

    public async Task<ProjectQueryResult> HandleAsync(ExecuteProjectDataCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Sql))
            throw new ArgumentException("SQL must not be empty.", nameof(command));
        _ = await _projects.GetByIdAsync(ProjectId.From(command.ProjectId), ct)
            ?? throw new InvalidOperationException($"Project {command.ProjectId} not found.");
        return await _store.ExecuteAsync(command.ProjectId, command.Sql, ct);
    }
}
