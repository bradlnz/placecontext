using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>Run SQL inside a project's own database (a command — SQL may write).</summary>
public sealed record ExecuteProjectDataCommand(Guid ProjectId, string Sql) : ICommand<ProjectQueryResult>;

/// <summary>List the tables in a project's own database.</summary>
public sealed record ListProjectDataTablesQuery(Guid ProjectId) : IQuery<IReadOnlyList<ProjectTableInfo>>;

/// <summary>Create a table in a project's database from a validated column spec.</summary>
public sealed record CreateProjectTableCommand(Guid ProjectId, string TableName, IReadOnlyList<ProjectColumnSpec> Columns) : ICommand<bool>;

/// <summary>Rename a table in a project's database.</summary>
public sealed record RenameProjectTableCommand(Guid ProjectId, string From, string To) : ICommand<bool>;

/// <summary>Drop a table from a project's database.</summary>
public sealed record DropProjectTableCommand(Guid ProjectId, string TableName) : ICommand<bool>;

/// <summary>Export a whole table as CSV.</summary>
public sealed record ExportProjectTableQuery(Guid ProjectId, string TableName) : IQuery<string>;

/// <summary>List the columns of one table in a project's database.</summary>
public sealed record ListProjectTableColumnsQuery(Guid ProjectId, string TableName) : IQuery<IReadOnlyList<ProjectColumnInfo>>;

/// <summary>Add a column to an existing table in a project's database.</summary>
public sealed record AddProjectTableColumnCommand(Guid ProjectId, string TableName, ProjectColumnSpec Column) : ICommand<bool>;

/// <summary>Drop a column from an existing table in a project's database.</summary>
public sealed record DropProjectTableColumnCommand(Guid ProjectId, string TableName, string ColumnName) : ICommand<bool>;

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

/// <summary>Shared guard: the project must exist before any data operation touches its store.</summary>
internal static class ProjectDataGuard
{
    public static async Task EnsureExistsAsync(IProjectRepository projects, Guid projectId, CancellationToken ct)
        => _ = await projects.GetByIdAsync(ProjectId.From(projectId), ct)
            ?? throw new InvalidOperationException($"Project {projectId} not found.");
}

public sealed class CreateProjectTableHandler : ICommandHandler<CreateProjectTableCommand, bool>
{
    private readonly IProjectRepository _projects;
    private readonly IProjectDataStore _store;
    public CreateProjectTableHandler(IProjectRepository projects, IProjectDataStore store) { _projects = projects; _store = store; }

    public async Task<bool> HandleAsync(CreateProjectTableCommand c, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(_projects, c.ProjectId, ct);
        await _store.CreateTableAsync(c.ProjectId, c.TableName, c.Columns, ct);
        return true;
    }
}

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

public sealed class DropProjectTableHandler : ICommandHandler<DropProjectTableCommand, bool>
{
    private readonly IProjectRepository _projects;
    private readonly IProjectDataStore _store;
    public DropProjectTableHandler(IProjectRepository projects, IProjectDataStore store) { _projects = projects; _store = store; }

    public async Task<bool> HandleAsync(DropProjectTableCommand c, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(_projects, c.ProjectId, ct);
        await _store.DropTableAsync(c.ProjectId, c.TableName, ct);
        return true;
    }
}

public sealed class ListProjectTableColumnsHandler : IQueryHandler<ListProjectTableColumnsQuery, IReadOnlyList<ProjectColumnInfo>>
{
    private readonly IProjectRepository _projects;
    private readonly IProjectDataStore _store;
    public ListProjectTableColumnsHandler(IProjectRepository projects, IProjectDataStore store) { _projects = projects; _store = store; }

    public async Task<IReadOnlyList<ProjectColumnInfo>> HandleAsync(ListProjectTableColumnsQuery q, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(_projects, q.ProjectId, ct);
        return await _store.ListColumnsAsync(q.ProjectId, q.TableName, ct);
    }
}

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

public sealed class ExportProjectTableHandler : IQueryHandler<ExportProjectTableQuery, string>
{
    private readonly IProjectRepository _projects;
    private readonly IProjectDataStore _store;
    public ExportProjectTableHandler(IProjectRepository projects, IProjectDataStore store) { _projects = projects; _store = store; }

    public async Task<string> HandleAsync(ExportProjectTableQuery q, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(_projects, q.ProjectId, ct);
        return await _store.ExportTableCsvAsync(q.ProjectId, q.TableName, ct);
    }
}
