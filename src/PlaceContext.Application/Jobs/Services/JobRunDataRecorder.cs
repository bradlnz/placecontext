using Microsoft.Extensions.Logging;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;

namespace PlaceContext.Application.Features;

/// <summary>
/// After a run completes, appends its results to the project's read-only <c>job_run_data</c> table —
/// one row per map shard plus one for the reduce step — so runs can be queried, joined, and charted
/// from the Data tab like any other project data. The table is system-owned: the store enforces that
/// the project can SELECT it but never write, alter, or drop it. Entirely best-effort — a data-store
/// failure is logged and never fails the run.
/// </summary>
public sealed class JobRunDataRecorder
{
    public const string TableName = "job_run_data";

    /// <summary>The table's shape. Order matters: rows are built positionally against this spec.</summary>
    internal static readonly IReadOnlyList<ProjectColumnSpec> Columns = new[]
    {
        new ProjectColumnSpec("recorded_at", "timestamptz", NotNull: true, PrimaryKey: false),
        new ProjectColumnSpec("run_id", "uuid", NotNull: true, PrimaryKey: false),
        new ProjectColumnSpec("job_id", "uuid", NotNull: true, PrimaryKey: false),
        new ProjectColumnSpec("job_name", "text", NotNull: true, PrimaryKey: false),
        new ProjectColumnSpec("run_status", "text", NotNull: true, PrimaryKey: false),
        new ProjectColumnSpec("step", "text", NotNull: true, PrimaryKey: false),
        new ProjectColumnSpec("shard_index", "integer", NotNull: false, PrimaryKey: false),
        new ProjectColumnSpec("exit_code", "integer", NotNull: true, PrimaryKey: false),
        new ProjectColumnSpec("outcome", "text", NotNull: true, PrimaryKey: false),
        new ProjectColumnSpec("artifact", "text", NotNull: false, PrimaryKey: false),
        new ProjectColumnSpec("started_at", "timestamptz", NotNull: true, PrimaryKey: false),
        new ProjectColumnSpec("finished_at", "timestamptz", NotNull: false, PrimaryKey: false),
    };

    private readonly IProjectDataStore _store;
    private readonly IClock _clock;
    private readonly ILogger<JobRunDataRecorder>? _log;

    public JobRunDataRecorder(IProjectDataStore store, IClock clock, ILogger<JobRunDataRecorder>? log = null)
    {
        _store = store;
        _clock = clock;
        _log = log;
    }

    public async Task RecordAsync(Job job, JobRun run, CancellationToken ct = default)
    {
        try
        {
            var rows = BuildRows(job, run, _clock.UtcNow);
            await _store.AppendReadOnlyRowsAsync(run.ProjectId, TableName, Columns, rows, ct);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Recording run {RunId} (job {JobId}) into {Table} failed.",
                run.Id, job.Id, TableName);
        }
    }

    internal static IReadOnlyList<IReadOnlyList<string?>> BuildRows(Job job, JobRun run, DateTimeOffset recordedAt)
    {
        var rows = new List<IReadOnlyList<string?>>(run.ShardResults.Count + 1);

        foreach (var shard in run.ShardResults.OrderBy(s => s.Index))
            rows.Add(Row(job, run, recordedAt, "map", shard.Index, shard.ExitCode, shard.Outcome.ToString(), shard.Artifact));

        if (run.ReduceResult is { } reduce)
            rows.Add(Row(job, run, recordedAt, "reduce", null, reduce.ExitCode,
                reduce.Succeeded ? "Succeeded" : "Failed", reduce.Artifact));

        return rows;
    }

    // Values travel as text and are cast by the store to each column's declared type.
    private static string?[] Row(Job job, JobRun run, DateTimeOffset recordedAt,
        string step, int? shardIndex, int exitCode, string outcome, string? artifact) => new[]
    {
        recordedAt.ToString("O"),
        run.Id.ToString(),
        job.Id.ToString(),
        job.Name,
        run.Status.ToString(),
        step,
        shardIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
        outcome,
        artifact,
        run.StartedAt.ToString("O"),
        run.FinishedAt?.ToString("O"),
    };
}
