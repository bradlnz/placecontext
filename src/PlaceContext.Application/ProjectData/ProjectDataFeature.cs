using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>Run SQL inside a project's own database (a command — SQL may write).</summary>
public sealed record ExecuteProjectDataCommand(Guid ProjectId, string Sql) : ICommand<ProjectQueryResult>;

/// <summary>List the tables in a project's own database.</summary>
public sealed record ListProjectDataTablesQuery(Guid ProjectId) : IQuery<IReadOnlyList<ProjectTableInfo>>;

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

public sealed class ListProjectDataTablesHandler : IQueryHandler<ListProjectDataTablesQuery, IReadOnlyList<ProjectTableInfo>>
{
    private readonly IProjectRepository _projects;
    private readonly IProjectDataStore _store;

    public ListProjectDataTablesHandler(IProjectRepository projects, IProjectDataStore store)
    {
        _projects = projects;
        _store = store;
    }

    public async Task<IReadOnlyList<ProjectTableInfo>> HandleAsync(ListProjectDataTablesQuery query, CancellationToken ct = default)
    {
        _ = await _projects.GetByIdAsync(ProjectId.From(query.ProjectId), ct)
            ?? throw new InvalidOperationException($"Project {query.ProjectId} not found.");
        return await _store.ListTablesAsync(query.ProjectId, ct);
    }
}
