using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>
/// Analytics charts: the background sweep samples each table and the deterministic builder shapes a
/// chart SPEC from the data itself (data only — Chart.js draws it in the portal); a table with
/// nothing chartable stores a themed HTML table instead. Charts of dropped tables are pruned.
/// </summary>
public class ProjectAnalyticsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 9, 10, 0, 0, TimeSpan.Zero);

    private sealed class FakeStore : IProjectDataStore
    {
        public List<string> SawSql = new();
        public IReadOnlyList<ProjectTableInfo> Tables = new[] { new ProjectTableInfo("readings", 2) };

        public Task<ProjectQueryResult> ExecuteAsync(Guid projectId, string sql, CancellationToken ct = default)
        {
            SawSql.Add(sql);
            return Task.FromResult(new ProjectQueryResult(
                new[] { "sensor", "value" },
                new[] { new string?[] { "door", "21.5" }, new string?[] { "window", "19.0" } },
                0, false));
        }

        public Task<IReadOnlyList<ProjectTableInfo>> ListTablesAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult(Tables);
        public Task CreateTableAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task RenameTableAsync(Guid projectId, string from, string to, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<IReadOnlyList<ProjectColumnInfo>> ListColumnsAsync(Guid projectId, string tableName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ProjectColumnInfo>>(Array.Empty<ProjectColumnInfo>());
        public Task AddColumnAsync(Guid projectId, string tableName, ProjectColumnSpec column, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task DropColumnAsync(Guid projectId, string tableName, string columnName, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task DropTableAsync(Guid projectId, string tableName, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<string> ExportTableCsvAsync(Guid projectId, string tableName, CancellationToken ct = default)
            => Task.FromResult("");
        public Task AppendReadOnlyRowsAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns,
            IReadOnlyList<IReadOnlyList<string?>> rows, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<int> ImportRowsAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns,
            IReadOnlyList<IReadOnlyList<string?>> rows, bool createTable, CancellationToken ct = default)
            => Task.FromResult(rows.Count);
        public Task<ProjectTablePageResult> QueryTablePageAsync(Guid projectId, string tableName, string? search,
            int page, int pageSize, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class InMemoryChartRepository : IProjectChartRepository
    {
        public readonly Dictionary<(Guid, string), ProjectChart> Charts = new();

        public Task UpsertAsync(ProjectChart chart, CancellationToken ct = default)
        {
            Charts[(chart.ProjectId, chart.TableName)] = chart;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ProjectChart>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ProjectChart>>(
                Charts.Values.Where(c => c.ProjectId == projectId).OrderBy(c => c.TableName).ToList());

        public Task DeleteAsync(Guid projectId, string tableName, CancellationToken ct = default)
        {
            Charts.Remove((projectId, tableName));
            return Task.CompletedTask;
        }

        public Task DeleteForProjectAsync(Guid projectId, IReadOnlyCollection<string> keepTables, CancellationToken ct = default)
        {
            foreach (var key in Charts.Keys.Where(k => k.Item1 == projectId && !keepTables.Contains(k.Item2)).ToList())
                Charts.Remove(key);
            return Task.CompletedTask;
        }
    }

    private static async Task<(InMemoryProjectRepository projects, Project project)> WorldAsync()
    {
        var projects = new InMemoryProjectRepository();
        var project = Project.Discover(ProjectPath.From("/repo/demo"), ProjectName.From("demo"), T0);
        await projects.AddAsync(project);
        return (projects, project);
    }

    private static ProjectChartService Service(IProjectDataStore store, InMemoryChartRepository charts)
        => new(store, charts, new RecordingUnitOfWork(), new FakeClock(T0));

    [Fact]
    public async Task Chart_spec_is_built_deterministically_from_the_table_rows()
    {
        var (projects, project) = await WorldAsync();
        var store = new FakeStore();
        var handler = new GenerateProjectChartHandler(projects, Service(store, new InMemoryChartRepository()));

        var stored = await handler.HandleAsync(new GenerateProjectChartCommand(project.Id.Value, "readings", null));

        var spec = ChartSpec.TryParse(stored);
        Assert.NotNull(spec);                          // a spec built from the data, stored as JSON
        Assert.Equal(new[] { "door", "window" }, spec!.Labels);
        Assert.Contains(21.5, spec.Series.SelectMany(s => s.Values)); // the data itself is charted
        Assert.Contains("readings", store.SawSql.Single());           // sampled the right table
    }

    [Fact]
    public async Task Unknown_projects_are_rejected()
    {
        var (projects, _) = await WorldAsync();
        var store = new FakeStore();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new GenerateProjectChartHandler(projects, Service(store, new InMemoryChartRepository()))
                .HandleAsync(new GenerateProjectChartCommand(Guid.NewGuid(), "readings", null)));
    }

    [Fact]
    public async Task Refresh_sweeps_every_table_storing_a_chart_per_table_and_prunes_dropped_ones()
    {
        var (_, project) = await WorldAsync();
        var store = new FakeStore
        {
            Tables = new[] { new ProjectTableInfo("readings", 2), new ProjectTableInfo("bookings", 5) },
        };
        var charts = new InMemoryChartRepository();
        // A chart for a table that no longer exists must be pruned by the sweep.
        charts.Charts[(project.Id.Value, "dropped_table")] =
            ProjectChart.Create(project.Id.Value, "dropped_table", "<html>old</html>", T0);

        await Service(store, charts).RefreshProjectAsync(project.Id.Value);

        var stored = await charts.ListForProjectAsync(project.Id.Value);
        Assert.Equal(new[] { "bookings", "readings" }, stored.Select(c => c.TableName));
        Assert.All(stored, c => Assert.NotNull(ChartSpec.TryParse(c.Html))); // specs, drawn by Chart.js
    }

    [Fact]
    public async Task Refresh_keeps_sweeping_when_one_table_fails()
    {
        var (_, project) = await WorldAsync();
        var store = new FailOnFirstStore
        {
            Tables = new[] { new ProjectTableInfo("bad", 1), new ProjectTableInfo("good", 1) },
        };
        var charts = new InMemoryChartRepository();

        // "bad" (its SELECT throws) is skipped; "good" still gets a stored chart.
        await Service(store, charts).RefreshProjectAsync(project.Id.Value);

        var stored = await charts.ListForProjectAsync(project.Id.Value);
        Assert.Equal(new[] { "good" }, stored.Select(c => c.TableName));
    }

    private sealed class FailOnFirstStore : IProjectDataStore
    {
        public IReadOnlyList<ProjectTableInfo> Tables = Array.Empty<ProjectTableInfo>();

        public Task<ProjectQueryResult> ExecuteAsync(Guid projectId, string sql, CancellationToken ct = default)
        {
            if (sql.Contains("\"bad\"")) throw new InvalidOperationException("relation exploded");
            return Task.FromResult(new ProjectQueryResult(new[] { "n" }, new[] { new string?[] { "1" } }, 0, false));
        }

        public Task<IReadOnlyList<ProjectTableInfo>> ListTablesAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult(Tables);
        public Task CreateTableAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task RenameTableAsync(Guid projectId, string from, string to, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<IReadOnlyList<ProjectColumnInfo>> ListColumnsAsync(Guid projectId, string tableName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ProjectColumnInfo>>(Array.Empty<ProjectColumnInfo>());
        public Task AddColumnAsync(Guid projectId, string tableName, ProjectColumnSpec column, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task DropColumnAsync(Guid projectId, string tableName, string columnName, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task DropTableAsync(Guid projectId, string tableName, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<string> ExportTableCsvAsync(Guid projectId, string tableName, CancellationToken ct = default)
            => Task.FromResult("");
        public Task AppendReadOnlyRowsAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns,
            IReadOnlyList<IReadOnlyList<string?>> rows, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<int> ImportRowsAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns,
            IReadOnlyList<IReadOnlyList<string?>> rows, bool createTable, CancellationToken ct = default)
            => Task.FromResult(rows.Count);
        public Task<ProjectTablePageResult> QueryTablePageAsync(Guid projectId, string tableName, string? search,
            int page, int pageSize, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
