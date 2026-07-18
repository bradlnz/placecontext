using System.Text.RegularExpressions;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests.DataLinks;

/// <summary>The write-path hooks: duplicate warnings ride the results, link refreshes follow writes,
/// and a failing link subsystem never changes the main outcome.</summary>
public class RecordLinkHookTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 18, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Pid = Guid.NewGuid();

    private static FakeDataStore StoreWithExistingSite()
    {
        var store = new FakeDataStore();
        store.Columns["sites"] = new[]
        {
            new ProjectColumnInfo("name", "text", false, false),
            new ProjectColumnInfo("address", "text", false, false),
        };
        store.Rows["sites"] = new[]
        {
            (IReadOnlyList<string?>)new string?[] { "HQ", "233 Gympie Rd" },
        };
        return store;
    }

    private static RecordLinkService Service(FakeDataStore store, FakeLinkStore links)
    {
        var entity = DataEntity.Create(Pid, "Sites", "sites", "name", Array.Empty<EntityRelation>(), T0);
        return new RecordLinkService(store, links, new FakeEntities(new[] { entity }));
    }

    [Fact]
    public async Task Create_returns_duplicate_warnings_and_still_inserts_and_refreshes()
    {
        var store = StoreWithExistingSite();
        var links = new FakeLinkStore();
        var handler = new CreateEntityRecordHandler(store, Service(store, links));
        var values = new Dictionary<string, string?> { ["name"] = "Depot", ["address"] = "233 Gympie Rd" };

        var result = await handler.HandleAsync(new CreateEntityRecordCommand(Pid, "sites", values));

        Assert.Equal("possible duplicate of HQ (shared address: 233 Gympie Rd)", Assert.Single(result.DuplicateWarnings));
        Assert.Single(store.Inserted); // warn-only: the row is always kept
        Assert.Equal(new[] { "sites" }, links.RefreshedTables);
    }

    [Fact]
    public async Task Create_succeeds_without_warnings_when_nothing_matches()
    {
        var store = StoreWithExistingSite();
        var handler = new CreateEntityRecordHandler(store, Service(store, new FakeLinkStore()));

        var result = await handler.HandleAsync(new CreateEntityRecordCommand(Pid, "sites",
            new Dictionary<string, string?> { ["name"] = "Depot", ["address"] = "9 Other Rd" }));

        Assert.Empty(result.DuplicateWarnings);
        Assert.Single(store.Inserted);
    }

    [Fact]
    public async Task Create_succeeds_even_when_the_link_service_throws()
    {
        var store = StoreWithExistingSite();
        store.BreakAllReads = true; // FindDuplicatesAsync throws; the handler must absorb it
        var handler = new CreateEntityRecordHandler(store, Service(store, new FakeLinkStore()));

        var result = await handler.HandleAsync(new CreateEntityRecordCommand(Pid, "sites",
            new Dictionary<string, string?> { ["name"] = "Depot", ["address"] = "233 Gympie Rd" }));

        Assert.Empty(result.DuplicateWarnings);
        Assert.Single(store.Inserted); // the insert went ahead regardless
    }

    [Fact]
    public async Task Update_and_delete_refresh_the_link_index_only_when_rows_changed()
    {
        var store = StoreWithExistingSite();
        var links = new FakeLinkStore();
        var service = Service(store, links);
        var keys = new Dictionary<string, string?> { ["name"] = "HQ" };

        store.UpdateAffected = 1;
        var updated = await new UpdateEntityRecordHandler(store, service).HandleAsync(
            new UpdateEntityRecordCommand(Pid, "sites", keys, new Dictionary<string, string?> { ["address"] = "x" }));
        Assert.Equal(1, updated);
        Assert.Equal(new[] { "sites" }, links.RefreshedTables);

        store.UpdateAffected = 0;
        await new UpdateEntityRecordHandler(store, service).HandleAsync(
            new UpdateEntityRecordCommand(Pid, "sites", keys, new Dictionary<string, string?> { ["address"] = "x" }));
        Assert.Equal(new[] { "sites" }, links.RefreshedTables); // unchanged: no rows, no refresh

        store.DeleteAffected = 1;
        var deleted = await new DeleteEntityRecordHandler(store, service).HandleAsync(
            new DeleteEntityRecordCommand(Pid, "sites", keys));
        Assert.Equal(1, deleted);
        Assert.Equal(new[] { "sites", "sites" }, links.RefreshedTables);
    }

    [Fact]
    public async Task Csv_import_returns_count_and_warnings_and_refreshes_the_table()
    {
        var projects = new InMemoryProjectRepository();
        var project = Project.Discover(ProjectPath.From("/repo/demo"), ProjectName.From("demo"), T0);
        await projects.AddAsync(project);
        var store = StoreWithExistingSite();
        var links = new FakeLinkStore();
        var handler = new ImportCsvToProjectTableHandler(projects, store, Service(store, links));
        var columns = new[]
        {
            new ProjectColumnSpec("name", "text", false, false),
            new ProjectColumnSpec("address", "text", false, false),
        };
        var rows = new IReadOnlyList<string?>[]
        {
            new string?[] { "Depot", "233 Gympie Rd" }, // duplicates the existing HQ row's address
            new string?[] { "Shop", "9 Other Rd" },
        };

        var result = await handler.HandleAsync(
            new ImportCsvToProjectTableCommand(project.Id.Value, "sites", columns, rows, CreateTable: false));

        Assert.Equal(2, result.Imported);
        Assert.Equal("possible duplicate of HQ (shared address: 233 Gympie Rd)", Assert.Single(result.DuplicateWarnings));
        Assert.Equal(new[] { "sites" }, links.RefreshedTables);
    }

    [Fact]
    public async Task Csv_import_without_matches_has_no_warnings()
    {
        var projects = new InMemoryProjectRepository();
        var project = Project.Discover(ProjectPath.From("/repo/demo"), ProjectName.From("demo"), T0);
        await projects.AddAsync(project);
        var store = StoreWithExistingSite();
        var handler = new ImportCsvToProjectTableHandler(projects, store, Service(store, new FakeLinkStore()));
        var columns = new[] { new ProjectColumnSpec("address", "text", false, false) };
        var rows = new IReadOnlyList<string?>[] { new string?[] { "9 Other Rd" } };

        var result = await handler.HandleAsync(
            new ImportCsvToProjectTableCommand(project.Id.Value, "sites", columns, rows, CreateTable: false));

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.DuplicateWarnings);
    }

    // ── fakes ────────────────────────────────────────────────────────────────────────────────

    private sealed class FakeDataStore : IProjectDataStore
    {
        public Dictionary<string, IReadOnlyList<ProjectColumnInfo>> Columns { get; } = new();
        public Dictionary<string, IReadOnlyList<IReadOnlyList<string?>>> Rows { get; } = new();
        public List<(string Table, IReadOnlyDictionary<string, string?> Values)> Inserted { get; } = new();
        public bool BreakAllReads { get; set; }
        public int UpdateAffected { get; set; }
        public int DeleteAffected { get; set; }

        public Task<IReadOnlyList<ProjectColumnInfo>> ListColumnsAsync(Guid projectId, string tableName, CancellationToken ct = default)
        {
            if (BreakAllReads) throw new InvalidOperationException("store is down");
            return Task.FromResult(Columns.TryGetValue(tableName, out var c) ? c : Array.Empty<ProjectColumnInfo>());
        }

        public Task<ProjectQueryResult> ExecuteAsync(Guid projectId, string sql, CancellationToken ct = default)
        {
            if (BreakAllReads) throw new InvalidOperationException("store is down");
            var table = Regex.Match(sql, "FROM \"([^\"]+)\"").Groups[1].Value;
            var selected = Regex.Matches(sql, "\"([^\"]+)\"::text").Select(m => m.Groups[1].Value).ToList();
            var all = Columns[table].Select(c => c.Name).ToList();
            var rows = Rows.TryGetValue(table, out var r) ? r : Array.Empty<IReadOnlyList<string?>>();
            var projected = rows.Select(row => (IReadOnlyList<string?>)selected
                .Select(c => all.IndexOf(c) is var i && i >= 0 && i < row.Count ? row[i] : null)
                .ToList()).ToList();
            return Task.FromResult(new ProjectQueryResult(selected, projected, 0, false));
        }

        public Task InsertRowAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> values, CancellationToken ct = default)
        { Inserted.Add((tableName, values)); return Task.CompletedTask; }

        public Task<int> UpdateRowsAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> keys,
            IReadOnlyDictionary<string, string?> values, CancellationToken ct = default)
            => Task.FromResult(UpdateAffected);

        public Task<int> DeleteRowsAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> keys, CancellationToken ct = default)
            => Task.FromResult(DeleteAffected);

        public Task<int> ImportRowsAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns,
            IReadOnlyList<IReadOnlyList<string?>> rows, bool createTable, CancellationToken ct = default)
            => Task.FromResult(rows.Count);

        public Task<IReadOnlyList<ProjectTableInfo>> ListTablesAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ProjectTableInfo>>(Array.Empty<ProjectTableInfo>());
        public Task<ProjectTablePageResult> QueryTablePageAsync(Guid projectId, string tableName, string? search,
            int page, int pageSize, CancellationToken ct = default) => throw new NotSupportedException();
        public Task CreateTableAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns, CancellationToken ct = default) => Task.CompletedTask;
        public Task RenameTableAsync(Guid projectId, string from, string to, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddColumnAsync(Guid projectId, string tableName, ProjectColumnSpec column, CancellationToken ct = default) => Task.CompletedTask;
        public Task DropColumnAsync(Guid projectId, string tableName, string columnName, CancellationToken ct = default) => Task.CompletedTask;
        public Task DropTableAsync(Guid projectId, string tableName, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> ExportTableCsvAsync(Guid projectId, string tableName, CancellationToken ct = default) => Task.FromResult("");
        public Task AppendReadOnlyRowsAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns,
            IReadOnlyList<IReadOnlyList<string?>> rows, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeLinkStore : IRecordLinkStore
    {
        public List<string> RefreshedTables { get; } = new();

        public Task ReplaceForProjectAsync(Guid projectId, IReadOnlyList<RecordLink> links, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ReplaceForTableAsync(Guid projectId, string table, IReadOnlyList<RecordLink> links, CancellationToken ct = default)
        { RefreshedTables.Add(table); return Task.CompletedTask; }

        public Task<IReadOnlyList<RecordLink>> RelatedAsync(Guid projectId, string table, string rowKey, int take = 20, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<RecordLinkGroup>> GroupsAsync(Guid projectId, int take = 50, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeEntities(IReadOnlyList<DataEntity> entities) : IDataEntityRepository
    {
        public Task<IReadOnlyList<DataEntity>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult(entities);
        public Task AddAsync(DataEntity entity, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(DataEntity entity, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid entityId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DataEntity?> GetByIdAsync(Guid entityId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
