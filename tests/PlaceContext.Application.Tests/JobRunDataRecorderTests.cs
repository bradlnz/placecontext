using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using Xunit;

namespace PlaceContext.Application.Tests;

public class JobRunDataRecorderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private static (Job job, JobRun run) Sample(ReduceResult? reduce = null)
    {
        var mapSpec = new MapSpec("img", new[] { "{}", "{}" }, new Dictionary<string, string>());
        var job = Job.Create(Guid.NewGuid(), "nightly-etl", null, mapSpec, null, 1,
            new ExitCodePolicy(new[] { 0 }, Array.Empty<int>()), T0);
        var run = JobRun.Start(job.Id, job.ProjectId, T0, WorkloadSnapshot.From(mapSpec, null, 1));
        run.Complete(new[]
        {
            new ShardResult(0, 0, WorkloadOutcome.Succeeded, "{\"n\":3}", "ok"),
            new ShardResult(1, 2, WorkloadOutcome.Failed, null, "boom"),
        }, reduce, T0.AddSeconds(2));
        return (job, run);
    }

    private sealed class CapturingStore : IProjectDataStore
    {
        public Guid? SawProject;
        public string? Table;
        public IReadOnlyList<ProjectColumnSpec>? Columns;
        public IReadOnlyList<IReadOnlyList<string?>>? Rows;

        public Task AppendReadOnlyRowsAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns,
            IReadOnlyList<IReadOnlyList<string?>> rows, CancellationToken ct = default)
        { SawProject = projectId; Table = tableName; Columns = columns; Rows = rows; return Task.CompletedTask; }

        public Task<ProjectQueryResult> ExecuteAsync(Guid projectId, string sql, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ProjectTableInfo>> ListTablesAsync(Guid projectId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task CreateTableAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task RenameTableAsync(Guid projectId, string from, string to, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ProjectColumnInfo>> ListColumnsAsync(Guid projectId, string tableName, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task AddColumnAsync(Guid projectId, string tableName, ProjectColumnSpec column, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task DropColumnAsync(Guid projectId, string tableName, string columnName, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task DropTableAsync(Guid projectId, string tableName, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<string> ExportTableCsvAsync(Guid projectId, string tableName, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => T0.AddSeconds(3);
    }

    [Fact]
    public async Task Records_one_row_per_shard_into_job_run_data()
    {
        var (job, run) = Sample();
        var store = new CapturingStore();

        await new JobRunDataRecorder(store, new FakeClock()).RecordAsync(job, run);

        Assert.Equal(run.ProjectId, store.SawProject);
        Assert.Equal("job_run_data", store.Table);
        Assert.Equal(2, store.Rows!.Count);

        // Rows line up positionally with the declared columns.
        var byName = store.Columns!.Select((c, i) => (c.Name, i)).ToDictionary(x => x.Name, x => x.i);
        var first = store.Rows[0];
        Assert.Equal(run.Id.ToString(), first[byName["run_id"]]);
        Assert.Equal(job.Id.ToString(), first[byName["job_id"]]);
        Assert.Equal("nightly-etl", first[byName["job_name"]]);
        Assert.Equal("map", first[byName["step"]]);
        Assert.Equal("0", first[byName["shard_index"]]);
        Assert.Equal("Succeeded", first[byName["outcome"]]);
        Assert.Equal("{\"n\":3}", first[byName["artifact"]]);

        var second = store.Rows[1];
        Assert.Equal("1", second[byName["shard_index"]]);
        Assert.Equal("2", second[byName["exit_code"]]);
        Assert.Equal("Failed", second[byName["outcome"]]);
        Assert.Null(second[byName["artifact"]]);
    }

    [Fact]
    public async Task Reduce_result_becomes_its_own_row_with_no_shard_index()
    {
        var (job, run) = Sample(new ReduceResult(0, true, "{\"total\":10}", null));
        var store = new CapturingStore();

        await new JobRunDataRecorder(store, new FakeClock()).RecordAsync(job, run);

        var byName = store.Columns!.Select((c, i) => (c.Name, i)).ToDictionary(x => x.Name, x => x.i);
        var reduceRow = Assert.Single(store.Rows!, r => r[byName["step"]] == "reduce");
        Assert.Null(reduceRow[byName["shard_index"]]);
        Assert.Equal("Succeeded", reduceRow[byName["outcome"]]);
        Assert.Equal("{\"total\":10}", reduceRow[byName["artifact"]]);
    }

    [Fact]
    public async Task A_failing_store_never_throws_out_of_the_recorder()
    {
        var (job, run) = Sample();
        var store = new ThrowingStore();

        // Best-effort: the run must never fail because run-data recording did.
        await new JobRunDataRecorder(store, new FakeClock()).RecordAsync(job, run);
    }

    private sealed class ThrowingStore : IProjectDataStore
    {
        public Task AppendReadOnlyRowsAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns,
            IReadOnlyList<IReadOnlyList<string?>> rows, CancellationToken ct = default)
            => throw new InvalidOperationException("db down");
        public Task<ProjectQueryResult> ExecuteAsync(Guid projectId, string sql, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ProjectTableInfo>> ListTablesAsync(Guid projectId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task CreateTableAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task RenameTableAsync(Guid projectId, string from, string to, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ProjectColumnInfo>> ListColumnsAsync(Guid projectId, string tableName, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task AddColumnAsync(Guid projectId, string tableName, ProjectColumnSpec column, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task DropColumnAsync(Guid projectId, string tableName, string columnName, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task DropTableAsync(Guid projectId, string tableName, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<string> ExportTableCsvAsync(Guid projectId, string tableName, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
