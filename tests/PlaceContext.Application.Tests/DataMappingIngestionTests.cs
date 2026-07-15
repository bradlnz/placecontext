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

    private static DataMappingIngestionService Service(Job job, DataMapping mapping, FakeDataStore store)
        => new(new FakeMappings(mapping), store, new FakeClock());

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
            => Task.FromResult<IReadOnlyList<ProjectColumnInfo>>(Array.Empty<ProjectColumnInfo>());
        public Task AddColumnAsync(Guid projectId, string tableName, ProjectColumnSpec column, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task DropColumnAsync(Guid projectId, string tableName, string columnName, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task DropTableAsync(Guid projectId, string tableName, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<string> ExportTableCsvAsync(Guid projectId, string tableName, CancellationToken ct = default)
            => Task.FromResult("");
        public Task<ProjectTablePageResult> QueryTablePageAsync(Guid projectId, string tableName, string? search,
            int page, int pageSize, CancellationToken ct = default)
            => throw new NotSupportedException();
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
