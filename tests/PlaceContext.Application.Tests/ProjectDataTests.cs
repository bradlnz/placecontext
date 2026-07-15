using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>Each project's own database: SQL runs only for real projects, and empty SQL is rejected.</summary>
public class ProjectDataTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 8, 10, 0, 0, TimeSpan.Zero);

    private sealed class FakeStore : IProjectDataStore
    {
        public Guid? SawProject;
        public string? SawSql;

        public Task<ProjectQueryResult> ExecuteAsync(Guid projectId, string sql, CancellationToken ct = default)
        {
            SawProject = projectId;
            SawSql = sql;
            return Task.FromResult(new ProjectQueryResult(new[] { "n" }, new[] { new string?[] { "1" } }, 0, false));
        }

        public Task<IReadOnlyList<ProjectTableInfo>> ListTablesAsync(Guid projectId, CancellationToken ct = default)
        {
            SawProject = projectId;
            return Task.FromResult<IReadOnlyList<ProjectTableInfo>>(new[] { new ProjectTableInfo("readings", 3) });
        }

        public string? CreatedTable;
        public (string From, string To)? Renamed;
        public string? Dropped;

        public Task CreateTableAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns, CancellationToken ct = default)
        { SawProject = projectId; CreatedTable = tableName; return Task.CompletedTask; }

        public Task RenameTableAsync(Guid projectId, string from, string to, CancellationToken ct = default)
        { SawProject = projectId; Renamed = (from, to); return Task.CompletedTask; }

        public Task DropTableAsync(Guid projectId, string tableName, CancellationToken ct = default)
        { SawProject = projectId; Dropped = tableName; return Task.CompletedTask; }

        public Task<string> ExportTableCsvAsync(Guid projectId, string tableName, CancellationToken ct = default)
        { SawProject = projectId; return Task.FromResult("a,b\n1,2\n"); }

        public (string Table, ProjectColumnSpec Column)? AddedColumn;
        public (string Table, string Column)? DroppedColumn;

        public Task<IReadOnlyList<ProjectColumnInfo>> ListColumnsAsync(Guid projectId, string tableName, CancellationToken ct = default)
        { SawProject = projectId; return Task.FromResult<IReadOnlyList<ProjectColumnInfo>>(new[] { new ProjectColumnInfo("id", "uuid", true, true) }); }

        public Task AddColumnAsync(Guid projectId, string tableName, ProjectColumnSpec column, CancellationToken ct = default)
        { SawProject = projectId; AddedColumn = (tableName, column); return Task.CompletedTask; }

        public Task DropColumnAsync(Guid projectId, string tableName, string columnName, CancellationToken ct = default)
        { SawProject = projectId; DroppedColumn = (tableName, columnName); return Task.CompletedTask; }

        public (string Table, IReadOnlyList<ProjectColumnSpec> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows)? Appended;

        public Task AppendReadOnlyRowsAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns,
            IReadOnlyList<IReadOnlyList<string?>> rows, CancellationToken ct = default)
        { SawProject = projectId; Appended = (tableName, columns, rows); return Task.CompletedTask; }
        public Task<int> ImportRowsAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns,
            IReadOnlyList<IReadOnlyList<string?>> rows, bool createTable, CancellationToken ct = default)
        { SawProject = projectId; Appended = (tableName, columns, rows); return Task.FromResult(rows.Count); }
    }

    private static async Task<(InMemoryProjectRepository projects, Project project, FakeStore store)> WorldAsync()
    {
        var projects = new InMemoryProjectRepository();
        var project = Project.Discover(ProjectPath.From("/repo/demo"), ProjectName.From("demo"), T0);
        await projects.AddAsync(project);
        return (projects, project, new FakeStore());
    }

    [Fact]
    public async Task Sql_executes_against_the_projects_store()
    {
        var (projects, project, store) = await WorldAsync();
        var handler = new ExecuteProjectDataHandler(projects, store);

        var result = await handler.HandleAsync(new ExecuteProjectDataCommand(project.Id.Value, "SELECT 1 AS n"));

        Assert.Equal(project.Id.Value, store.SawProject);
        Assert.Equal("SELECT 1 AS n", store.SawSql);
        Assert.Equal(new[] { "n" }, result.Columns);
    }

    [Fact]
    public async Task Unknown_project_and_empty_sql_are_rejected()
    {
        var (projects, project, store) = await WorldAsync();
        var handler = new ExecuteProjectDataHandler(projects, store);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new ExecuteProjectDataCommand(Guid.NewGuid(), "SELECT 1")));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.HandleAsync(new ExecuteProjectDataCommand(project.Id.Value, "  ")));
        Assert.Null(store.SawSql);
    }

    [Fact]
    public async Task Tables_list_comes_from_the_projects_store()
    {
        var (projects, project, store) = await WorldAsync();
        var handler = new ListProjectDataTablesHandler(projects, store);

        var tables = await handler.HandleAsync(new ListProjectDataTablesQuery(project.Id.Value));

        Assert.Single(tables);
        Assert.Equal("readings", tables[0].Name);
    }

    [Fact]
    public async Task Create_rename_drop_and_export_reach_the_store_for_a_real_project()
    {
        var (projects, project, store) = await WorldAsync();
        var pid = project.Id.Value;

        await new CreateProjectTableHandler(projects, store).HandleAsync(
            new CreateProjectTableCommand(pid, "events", new[] { new ProjectColumnSpec("id", "uuid", true, true) }));
        await new RenameProjectTableHandler(projects, store).HandleAsync(new RenameProjectTableCommand(pid, "events", "log"));
        await new DropProjectTableHandler(projects, store).HandleAsync(new DropProjectTableCommand(pid, "log"));
        var csv = await new ExportProjectTableHandler(projects, store).HandleAsync(new ExportProjectTableQuery(pid, "readings"));

        Assert.Equal("events", store.CreatedTable);
        Assert.Equal(("events", "log"), store.Renamed);
        Assert.Equal("log", store.Dropped);
        Assert.Contains("a,b", csv);
    }

    [Fact]
    public async Task Column_list_add_and_drop_reach_the_store_for_a_real_project()
    {
        var (projects, project, store) = await WorldAsync();
        var pid = project.Id.Value;

        var cols = await new ListProjectTableColumnsHandler(projects, store).HandleAsync(
            new ListProjectTableColumnsQuery(pid, "readings"));
        await new AddProjectTableColumnHandler(projects, store).HandleAsync(
            new AddProjectTableColumnCommand(pid, "readings", new ProjectColumnSpec("note", "text", false, false)));
        await new DropProjectTableColumnHandler(projects, store).HandleAsync(
            new DropProjectTableColumnCommand(pid, "readings", "note"));

        Assert.Single(cols);
        Assert.Equal("id", cols[0].Name);
        Assert.Equal(("readings", new ProjectColumnSpec("note", "text", false, false)), store.AddedColumn);
        Assert.Equal(("readings", "note"), store.DroppedColumn);
    }

    [Fact]
    public async Task Column_ops_reject_an_unknown_project()
    {
        var (projects, _, store) = await WorldAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AddProjectTableColumnHandler(projects, store).HandleAsync(
                new AddProjectTableColumnCommand(Guid.NewGuid(), "t", new ProjectColumnSpec("note", "text", false, false))));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DropProjectTableColumnHandler(projects, store).HandleAsync(
                new DropProjectTableColumnCommand(Guid.NewGuid(), "t", "note")));
        Assert.Null(store.AddedColumn);
        Assert.Null(store.DroppedColumn);
    }

    [Fact]
    public async Task Table_ops_reject_an_unknown_project()
    {
        var (projects, _, store) = await WorldAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CreateProjectTableHandler(projects, store).HandleAsync(
                new CreateProjectTableCommand(Guid.NewGuid(), "t", new[] { new ProjectColumnSpec("id", "uuid", true, true) })));
        Assert.Null(store.CreatedTable);
    }
}
