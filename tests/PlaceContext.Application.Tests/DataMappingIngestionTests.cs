using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Xunit;

namespace PlaceContext.Application.Tests;

public class DataMappingIngestionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 12, 9, 0, 0, TimeSpan.Zero);

    private static (Job job, JobRun run) RunWithArtifact(string artifact)
    {
        var mapSpec = new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>());
        var job = Job.Create(Guid.NewGuid(), "collector", null, mapSpec, null, 1,
            new ExitCodePolicy(new[] { 0 }, Array.Empty<int>()), T0);
        var run = JobRun.Start(job.Id, job.ProjectId, T0, WorkloadSnapshot.From(mapSpec, null, 1));
        run.Complete(new[] { new ShardResult(0, 0, WorkloadOutcome.Succeeded, artifact, "ok") }, null, T0.AddSeconds(1));
        return (job, run);
    }

    private static DataMappingIngestionService Service(Job job, DataMapping mapping, FakeDataStore store,
        FakeNotifier? notifier = null)
        => new(new FakeMappings(mapping), store, new FakeClock(), indexer: null, notifier: notifier);

    [Fact]
    public async Task Ingests_each_record_of_the_rows_path_array_with_provenance()
    {
        var (job, run) = RunWithArtifact("""{"rows":[{"city":"Brisbane","price":740000},{"city":"Ipswich","price":520000}]}""");
        var mapping = DataMapping.Create(job.ProjectId, job.Id, "listings", "rows", new[]
        {
            new DataFieldMapping("city", "city", "text"),
            new DataFieldMapping("price", "price", "numeric"),
        }, T0);
        var store = new FakeDataStore();

        await Service(job, mapping, store).IngestAsync(job, run);

        var append = Assert.Single(store.Appends);
        Assert.Equal("listings", append.Table);
        Assert.Equal(new[] { "ingested_at", "run_id", "city", "price" }, append.Columns.Select(c => c.Name));
        Assert.Equal(2, append.Rows.Count);
        Assert.Equal(run.Id.ToString(), append.Rows[0][1]);
        Assert.Equal("Brisbane", append.Rows[0][2]);
        Assert.Equal("740000", append.Rows[0][3]);
        Assert.Equal("Ipswich", append.Rows[1][2]);
    }

    [Fact]
    public async Task A_single_object_ingests_as_one_row_and_nested_paths_resolve()
    {
        var (job, run) = RunWithArtifact("""{"summary":{"total":42,"meta":{"region":"QLD"}}}""");
        var mapping = DataMapping.Create(job.ProjectId, job.Id, "totals", "summary", new[]
        {
            new DataFieldMapping("total", "total", "integer"),
            new DataFieldMapping("meta.region", "region", "text"),
        }, T0);
        var store = new FakeDataStore();

        await Service(job, mapping, store).IngestAsync(job, run);

        var append = Assert.Single(store.Appends);
        var row = Assert.Single(append.Rows);
        Assert.Equal("42", row[2]);
        Assert.Equal("QLD", row[3]);
    }

    [Fact]
    public async Task Disabled_mappings_and_missing_paths_ingest_nothing_and_never_throw()
    {
        var (job, run) = RunWithArtifact("""{"rows":[{"a":1}]}""");
        var disabled = DataMapping.Create(job.ProjectId, job.Id, "t1", "rows",
            new[] { new DataFieldMapping("a", "a", "integer") }, T0, enabled: false);
        var store = new FakeDataStore();
        await Service(job, disabled, store).IngestAsync(job, run);
        Assert.Empty(store.Appends);

        var missingPath = DataMapping.Create(job.ProjectId, job.Id, "t2", "nope.here",
            new[] { new DataFieldMapping("a", "a", "integer") }, T0);
        await Service(job, missingPath, store).IngestAsync(job, run);
        Assert.Empty(store.Appends);
    }

    [Fact]
    public async Task A_field_targeting_a_missing_column_on_an_existing_table_drops_nothing_and_surfaces_a_notification()
    {
        var (job, run) = RunWithArtifact("""{"listings":[{"suburb":"Logan","lga":"Logan"}]}""");
        var mapping = DataMapping.Create(job.ProjectId, job.Id, "listings", "listings", new[]
        {
            new DataFieldMapping("suburb", "suburb", "text"),
            new DataFieldMapping("lga", "lga", "text"), // table has council_code, not lga
        }, T0);
        var store = new FakeDataStore();
        store.Existing["listings"] = new[]
        {
            new ProjectColumnInfo("suburb", "text", false, false),
            new ProjectColumnInfo("council_code", "text", false, false),
            new ProjectColumnInfo("ingested_at", "timestamptz", true, false),
            new ProjectColumnInfo("run_id", "uuid", true, false),
        };
        var notifier = new FakeNotifier();

        await Service(job, mapping, store, notifier).IngestAsync(job, run);

        Assert.Empty(store.Appends); // the doomed insert is skipped, not attempted row-by-row
        var update = Assert.Single(notifier.Updates);
        Assert.Equal(RunOutcome.Failed, update.Outcome);
        Assert.Contains("'lga'", update.Detail);
        Assert.Contains("council_code", update.Detail);
    }

    [Fact]
    public async Task All_columns_present_on_an_existing_table_ingests_normally()
    {
        var (job, run) = RunWithArtifact("""{"listings":[{"suburb":"Logan","lga":"Logan"}]}""");
        var mapping = DataMapping.Create(job.ProjectId, job.Id, "listings", "listings", new[]
        {
            new DataFieldMapping("suburb", "suburb", "text"),
            new DataFieldMapping("lga", "council_code", "text"), // source lga → existing council_code
        }, T0);
        var store = new FakeDataStore();
        store.Existing["listings"] = new[]
        {
            new ProjectColumnInfo("suburb", "text", false, false),
            new ProjectColumnInfo("council_code", "text", false, false),
        };
        var notifier = new FakeNotifier();

        await Service(job, mapping, store, notifier).IngestAsync(job, run);

        var append = Assert.Single(store.Appends);
        Assert.Equal("Logan", append.Rows[0][3]); // council_code value
        Assert.Empty(notifier.Updates);
    }

    [Fact]
    public async Task An_object_valued_field_stays_as_a_json_blob_in_its_declared_column()
    {
        var (job, run) = RunWithArtifact("""
            {"rows":[
              {"city":"Brisbane","meta":{"region":"QLD","pop":2500000,"capital":true}},
              {"city":"Ipswich","meta":{"region":"QLD","pop":240000,"capital":false}}
            ]}
            """);
        var mapping = DataMapping.Create(job.ProjectId, job.Id, "listings", "rows", new[]
        {
            new DataFieldMapping("city", "city", "text"),
            new DataFieldMapping("meta", "meta", "jsonb"),
        }, T0);
        var store = new FakeDataStore();

        await Service(job, mapping, store).IngestAsync(job, run);

        var append = Assert.Single(store.Appends);
        // Objects are no longer flattened — they land as JSON text in the declared column.
        Assert.Equal(new[] { "ingested_at", "run_id", "city", "meta" }, append.Columns.Select(c => c.Name));
        Assert.Equal(new[] { "timestamptz", "uuid", "text", "jsonb" }, append.Columns.Select(c => c.Type));
        Assert.Equal(2, append.Rows.Count);
        Assert.Equal("Brisbane", append.Rows[0][2]);
        Assert.Equal("""{"region":"QLD","pop":2500000,"capital":true}""", append.Rows[0][3]);
    }

    [Fact]
    public async Task Nested_objects_stay_as_json_in_the_declared_column()
    {
        var (job, run) = RunWithArtifact("""{"rows":[{"meta":{"a":{"b":{"c":"deep"}}}}]}""");
        var mapping = DataMapping.Create(job.ProjectId, job.Id, "t", "rows",
            new[] { new DataFieldMapping("meta", "meta", "jsonb") }, T0);
        var store = new FakeDataStore();

        await Service(job, mapping, store).IngestAsync(job, run);

        var append = Assert.Single(store.Appends);
        Assert.Equal(new[] { "ingested_at", "run_id", "meta" }, append.Columns.Select(c => c.Name));
        Assert.Equal("""{"a":{"b":{"c":"deep"}}}""", append.Rows[0][2]);
    }

    [Fact]
    public async Task Arrays_and_empty_objects_stay_in_the_declared_column()
    {
        var (job, run) = RunWithArtifact("""{"rows":[{"tags":["a","b"],"extra":{}}]}""");
        var mapping = DataMapping.Create(job.ProjectId, job.Id, "t", "rows", new[]
        {
            new DataFieldMapping("tags", "tags", "jsonb"),
            new DataFieldMapping("extra", "extra", "jsonb"),
        }, T0);
        var store = new FakeDataStore();

        await Service(job, mapping, store).IngestAsync(job, run);

        var append = Assert.Single(store.Appends);
        Assert.Equal(new[] { "ingested_at", "run_id", "tags", "extra" }, append.Columns.Select(c => c.Name));
        Assert.Equal("""["a","b"]""", append.Rows[0][2]);
        Assert.Equal("{}", append.Rows[0][3]);
    }

    [Fact]
    public async Task Mixed_object_and_scalar_rows_both_land_in_the_declared_column()
    {
        var (job, run) = RunWithArtifact("""{"rows":[{"meta":{"region":"QLD"}},{"meta":"unknown"}]}""");
        var mapping = DataMapping.Create(job.ProjectId, job.Id, "t", "rows",
            new[] { new DataFieldMapping("meta", "meta", "text") }, T0);
        var store = new FakeDataStore();

        await Service(job, mapping, store).IngestAsync(job, run);

        var append = Assert.Single(store.Appends);
        Assert.Equal(new[] { "ingested_at", "run_id", "meta" }, append.Columns.Select(c => c.Name));
        Assert.Equal("""{"region":"QLD"}""", append.Rows[0][2]); // object row: JSON text
        Assert.Equal("unknown", append.Rows[1][2]);                  // scalar row: plain text
    }

    [Fact]
    public async Task Object_fields_use_the_declared_column_and_pass_the_existing_table_guard()
    {
        var (job, run) = RunWithArtifact("""{"rows":[{"city":"Logan","meta":{"region":"QLD"}}]}""");
        var mapping = DataMapping.Create(job.ProjectId, job.Id, "listings", "rows", new[]
        {
            new DataFieldMapping("city", "city", "text"),
            new DataFieldMapping("meta", "meta", "jsonb"),
        }, T0);
        var store = new FakeDataStore();
        store.Existing["listings"] = new[]
        {
            new ProjectColumnInfo("ingested_at", "timestamptz", true, false),
            new ProjectColumnInfo("run_id", "uuid", true, false),
            new ProjectColumnInfo("city", "text", false, false),
            new ProjectColumnInfo("meta", "jsonb", false, false),
        };
        var notifier = new FakeNotifier();

        await Service(job, mapping, store, notifier).IngestAsync(job, run);

        var append = Assert.Single(store.Appends);
        Assert.Equal(new[] { "ingested_at", "run_id", "city", "meta" }, append.Columns.Select(c => c.Name));
        Assert.Equal("""{"region":"QLD"}""", append.Rows[0][3]);
        Assert.Empty(notifier.Updates);
    }

    [Fact]
    public async Task Object_fields_with_sanitizing_name_overlap_both_keep_their_declared_columns()
    {
        // With flattening disabled, m and m_a are distinct declared columns and both values survive.
        var (job, run) = RunWithArtifact("""{"rows":[{"m":{"a_b":"first"},"m_a":{"b":"second"}}]}""");
        var mapping = DataMapping.Create(job.ProjectId, job.Id, "t", "rows", new[]
        {
            new DataFieldMapping("m", "m", "jsonb"),
            new DataFieldMapping("m_a", "m_a", "jsonb"),
        }, T0);
        var store = new FakeDataStore();

        await Service(job, mapping, store).IngestAsync(job, run);

        var append = Assert.Single(store.Appends);
        Assert.Equal(new[] { "ingested_at", "run_id", "m", "m_a" }, append.Columns.Select(c => c.Name));
        var row = Assert.Single(append.Rows);
        Assert.Equal("""{"a_b":"first"}""", row[2]);
        Assert.Equal("""{"b":"second"}""", row[3]);
    }

    // ── fakes ───────────────────────────────────────────────────────────────────────────────────────

    private sealed class FakeMappings(DataMapping mapping) : IDataMappingRepository
    {
        public Task AddAsync(DataMapping m, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(DataMapping m, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<DataMapping?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<DataMapping?>(mapping.Id == id ? mapping : null);
        public Task<IReadOnlyList<DataMapping>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DataMapping>>(new[] { mapping });
        public Task<IReadOnlyList<DataMapping>> ListForJobAsync(Guid jobId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DataMapping>>(mapping.JobId == jobId ? new[] { mapping } : Array.Empty<DataMapping>());
    }

    private sealed class FakeDataStore : IProjectDataStore
    {
        public List<(string Table, IReadOnlyList<ProjectColumnSpec> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows)> Appends { get; } = new();

        /// <summary>Seed a table's existing columns; absent tables report empty (i.e. not yet created).</summary>
        public Dictionary<string, IReadOnlyList<ProjectColumnInfo>> Existing { get; } = new();

        public Task AppendReadOnlyRowsAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns,
            IReadOnlyList<IReadOnlyList<string?>> rows, CancellationToken ct = default)
        {
            Appends.Add((tableName, columns, rows));
            return Task.CompletedTask;
        }

        public Task<int> ImportRowsAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns,
            IReadOnlyList<IReadOnlyList<string?>> rows, bool createTable, CancellationToken ct = default)
            => Task.FromResult(rows.Count);

        public Task InsertRowAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> values, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<int> UpdateRowsAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> keys,
            IReadOnlyDictionary<string, string?> values, CancellationToken ct = default)
            => Task.FromResult(0);
        public Task<int> DeleteRowsAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> keys, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<ProjectQueryResult> ExecuteAsync(Guid projectId, string sql, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ProjectTableInfo>> ListTablesAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ProjectTableInfo>>(Array.Empty<ProjectTableInfo>());
        public Task CreateTableAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task RenameTableAsync(Guid projectId, string from, string to, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<IReadOnlyList<ProjectColumnInfo>> ListColumnsAsync(Guid projectId, string tableName, CancellationToken ct = default)
            => Task.FromResult(Existing.TryGetValue(tableName, out var cols) ? cols : Array.Empty<ProjectColumnInfo>());
        public Task AddColumnAsync(Guid projectId, string tableName, ProjectColumnSpec column, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task DropColumnAsync(Guid projectId, string tableName, string columnName, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task DropTableAsync(Guid projectId, string tableName, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<string> ExportTableCsvAsync(Guid projectId, string tableName, CancellationToken ct = default)
            => Task.FromResult("");
        public Task<ProjectTablePageResult> QueryTablePageAsync(Guid projectId, string tableName, string? search,
            int page, int pageSize, string? sortColumn = null, bool sortDescending = false, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeNotifier : IRunStatusNotifier
    {
        public List<RunStatusUpdate> Updates { get; } = new();
        public void Sync(RunStatusUpdate update) => Updates.Add(update);
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => T0;
    }
        public Task InsertRowAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> values, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<int> UpdateRowsAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> keys,
            IReadOnlyDictionary<string, string?> values, CancellationToken ct = default)
            => Task.FromResult(0);
        public Task<int> DeleteRowsAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> keys, CancellationToken ct = default)
            => Task.FromResult(0);

}
