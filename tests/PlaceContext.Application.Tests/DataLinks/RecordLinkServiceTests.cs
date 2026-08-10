using System.Text.RegularExpressions;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Xunit;

namespace PlaceContext.Application.Tests.DataLinks;

/// <summary>The record-link scan/refresh/duplicate pipeline over fake project stores.</summary>
public class RecordLinkServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 18, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Pid = Guid.NewGuid();

    private static FakeDataStore SitesTable()
    {
        var store = new FakeDataStore();
        store.Tables.Add(new ProjectTableInfo("sites", 2));
        store.Columns["sites"] = new[]
        {
            new ProjectColumnInfo("id", "uuid", true, true),
            new ProjectColumnInfo("name", "text", false, false),
            new ProjectColumnInfo("address", "text", false, false),
            new ProjectColumnInfo("suburb", "text", false, false),
        };
        store.Rows["sites"] = new[]
        {
            (IReadOnlyList<string?>)new string?[] { "1", "HQ", "233 Gympie Rd", "Gympie" },
            (IReadOnlyList<string?>)new string?[] { "2", "Depot", "1 Main St", "Logan" },
        };
        return store;
    }

    private static RecordLinkService Service(FakeDataStore store, FakeLinkStore links, params DataEntity[] entities)
        => new(store, links, new FakeEntities(entities));

    [Fact]
    public async Task Rescan_indexes_identity_columns_and_skips_tables_without_any()
    {
        var store = SitesTable();
        store.Tables.Add(new ProjectTableInfo("metrics", 1)); // no identity columns
        store.Columns["metrics"] = new[] { new ProjectColumnInfo("total", "numeric", false, false) };
        var links = new FakeLinkStore();

        var result = await Service(store, links).RescanProjectAsync(Pid);

        Assert.Equal(1, result.TablesScanned); // metrics skipped
        Assert.Equal(5, result.LinksFound);    // identity cells minus "HQ" (too short to link)
        Assert.Equal(5, links.Current.Count);
        Assert.All(links.Current, l => Assert.Equal("sites", l.TableName));
        var address = links.Current.Single(l => l.ColumnName == "address" && l.NormalizedValue == "233 gympie rd");
        Assert.Equal("address", address.Kind);
        Assert.Equal("233 Gympie Rd", address.DisplayValue);
        Assert.Equal("HQ · 233 Gympie Rd · Gympie", address.RowKey); // first ≤ 3 text columns
        Assert.Equal("name", links.Current.Single(l => l.ColumnName == "name").Kind);
        Assert.Equal(1, links.ProjectReplaces);
    }

    [Fact]
    public async Task Rescan_keys_rows_by_the_entity_label_column_when_the_table_backs_an_entity()
    {
        var store = SitesTable();
        var links = new FakeLinkStore();
        var entity = DataEntity.Create(Pid, "Sites", "sites", "name", Array.Empty<EntityRelation>(), T0);

        await Service(store, links, entity).RescanProjectAsync(Pid);

        Assert.Equal(5, links.Current.Count);
        Assert.Equal("HQ", links.Current.Where(l => l.DisplayValue == "233 Gympie Rd").Single().RowKey);
        Assert.Equal("Depot", links.Current.Where(l => l.DisplayValue == "1 Main St").Single().RowKey);
    }

    [Fact]
    public async Task Rescan_continues_past_a_failing_table_and_never_throws()
    {
        var store = SitesTable();
        store.Tables.Add(new ProjectTableInfo("broken", 1));
        store.FailingTables.Add("broken");
        var links = new FakeLinkStore();

        var result = await Service(store, links).RescanProjectAsync(Pid);

        Assert.Equal(1, result.TablesScanned);
        Assert.Equal(5, links.Current.Count);
    }

    [Fact]
    public async Task Refresh_replaces_only_that_tables_slice()
    {
        var store = SitesTable();
        var links = new FakeLinkStore();
        links.Seed(
            new RecordLink(Pid, "address", "stale", "stale", "sites", "address", "old"),
            new RecordLink(Pid, "name", "other", "other", "other_table", "name", "x"));

        await Service(store, links).RefreshTableAsync(Pid, "sites");

        Assert.DoesNotContain(links.Current, l => l.NormalizedValue == "stale");
        Assert.Contains(links.Current, l => l.TableName == "other_table");
        Assert.Equal(6, links.Current.Count); // 5 fresh sites links + the untouched other-table link
        Assert.Equal(new[] { "sites" }, links.RefreshedTables);
    }

    [Fact]
    public async Task Refresh_never_throws_when_the_table_read_fails()
    {
        var store = SitesTable();
        store.FailingTables.Add("sites");
        var links = new FakeLinkStore();

        await Service(store, links).RefreshTableAsync(Pid, "sites"); // must not throw

        Assert.Empty(links.RefreshedTables);
    }

    [Fact]
    public async Task FindDuplicates_warns_when_a_new_rows_identity_value_already_exists()
    {
        var store = SitesTable();
        var entity = DataEntity.Create(Pid, "Sites", "sites", "name", Array.Empty<EntityRelation>(), T0);
        var service = Service(store, new FakeLinkStore(), entity);
        var newRows = new IReadOnlyDictionary<string, string?>[]
        {
            new Dictionary<string, string?> { ["Address"] = "233  Gympie  RD", ["name"] = "Warehouse" },
            new Dictionary<string, string?> { ["address"] = "9 Other Rd" },
        };

        var warnings = await service.FindDuplicatesAsync(Pid, "sites", newRows);

        var warning = Assert.Single(warnings);
        Assert.Equal("possible duplicate of HQ (shared address: 233  Gympie  RD)", warning);
    }

    [Fact]
    public async Task FindDuplicates_returns_empty_without_identity_columns_or_new_rows()
    {
        var store = new FakeDataStore();
        store.Columns["metrics"] = new[] { new ProjectColumnInfo("total", "numeric", false, false) };
        var service = Service(store, new FakeLinkStore());
        var rows = new IReadOnlyDictionary<string, string?>[]
        {
            new Dictionary<string, string?> { ["total"] = "42" },
        };

        Assert.Empty(await service.FindDuplicatesAsync(Pid, "metrics", rows)); // no identity columns
        Assert.Empty(await service.FindDuplicatesAsync(Pid, "metrics", Array.Empty<IReadOnlyDictionary<string, string?>>())); // no rows
    }

    [Fact]
    public async Task FindDuplicates_ignores_values_not_worth_linking()
    {
        var store = new FakeDataStore();
        store.Columns["sites"] = new[]
        {
            new ProjectColumnInfo("address", "text", false, false),
        };
        store.Rows["sites"] = new[]
        {
            (IReadOnlyList<string?>)new string?[] { "4000" },   // pure number
            (IReadOnlyList<string?>)new string?[] { "ab" },      // too short
            (IReadOnlyList<string?>)new string?[] { "true" },    // boolean
        };
        var service = Service(store, new FakeLinkStore());
        var rows = new IReadOnlyDictionary<string, string?>[]
        {
            new Dictionary<string, string?> { ["address"] = "4000" },
            new Dictionary<string, string?> { ["address"] = "ab" },
            new Dictionary<string, string?> { ["address"] = "true" },
        };

        Assert.Empty(await service.FindDuplicatesAsync(Pid, "sites", rows));
    }

    // ── fakes ────────────────────────────────────────────────────────────────────────────────

    private sealed class FakeDataStore : IProjectDataStore
    {
        public List<ProjectTableInfo> Tables { get; } = new();
        public Dictionary<string, IReadOnlyList<ProjectColumnInfo>> Columns { get; } = new();
        public Dictionary<string, IReadOnlyList<IReadOnlyList<string?>>> Rows { get; } = new();
        public HashSet<string> FailingTables { get; } = new();
        public List<(string Table, IReadOnlyDictionary<string, string?> Values)> Inserted { get; } = new();
        public int UpdateAffected { get; set; }
        public int DeleteAffected { get; set; }

        public Task<ProjectTableReadResult> ReadTableAsync(Guid projectId, string tableName, long maxRows = 10000, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ProjectTableInfo>> ListTablesAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ProjectTableInfo>>(Tables);

        public Task<IReadOnlyList<ProjectColumnInfo>> ListColumnsAsync(Guid projectId, string tableName, CancellationToken ct = default)
        {
            if (FailingTables.Contains(tableName)) throw new InvalidOperationException($"table '{tableName}' is broken");
            return Task.FromResult(Columns.TryGetValue(tableName, out var c) ? c : Array.Empty<ProjectColumnInfo>());
        }

        // Projects the seeded full rows down to the SELECTed columns, like the real store would.
        public Task<ProjectQueryResult> ExecuteAsync(Guid projectId, string sql, CancellationToken ct = default)
        {
            var table = Regex.Match(sql, "FROM \"([^\"]+)\"").Groups[1].Value;
            if (FailingTables.Contains(table)) throw new InvalidOperationException($"table '{table}' is broken");
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

        public Task<ProjectTablePageResult> QueryTablePageAsync(Guid projectId, string tableName, string? search,
            int page, int pageSize, string? sortColumn = null, bool sortDescending = false, CancellationToken ct = default) => throw new NotSupportedException();
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
        public List<RecordLink> Current { get; private set; } = new();
        public List<string> RefreshedTables { get; } = new();
        public int ProjectReplaces { get; private set; }

        public void Seed(params RecordLink[] links) => Current.AddRange(links);

        public Task ReplaceForProjectAsync(Guid projectId, IReadOnlyList<RecordLink> links, CancellationToken ct = default)
        { Current = links.ToList(); ProjectReplaces++; return Task.CompletedTask; }

        public Task ReplaceForTableAsync(Guid projectId, string table, IReadOnlyList<RecordLink> links, CancellationToken ct = default)
        {
            RefreshedTables.Add(table);
            Current = Current.Where(l => l.TableName != table).Concat(links).ToList();
            return Task.CompletedTask;
        }

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
