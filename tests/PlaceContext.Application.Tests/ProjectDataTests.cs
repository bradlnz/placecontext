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
    public async Task Table_ops_reject_an_unknown_project()
    {
        var (projects, _, store) = await WorldAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CreateProjectTableHandler(projects, store).HandleAsync(
                new CreateProjectTableCommand(Guid.NewGuid(), "t", new[] { new ProjectColumnSpec("id", "uuid", true, true) })));
        Assert.Null(store.CreatedTable);
    }
}
