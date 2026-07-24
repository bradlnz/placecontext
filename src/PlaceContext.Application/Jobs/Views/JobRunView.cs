namespace PlaceContext.Application.Dtos;

/// <summary>Read model for a job run summary (list view).</summary>
public sealed record JobRunView(
    Guid Id,
    Guid JobId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int ShardCount,
    int SucceededShards,
    int PartialShards,
    int FailedShards);

/// <summary>Read model for a single shard result.</summary>
public sealed record ShardResultView(
    int Index,
    int ExitCode,
    string Outcome,
    string? Artifact,
    string? Log,
    IReadOnlyList<RunArtifactView> Artifacts);

/// <summary>Read model for the reduce result.</summary>
public sealed record ReduceResultView(
    int ExitCode,
    bool Succeeded,
    string? Artifact,
    string? Log,
    IReadOnlyList<RunArtifactView> Artifacts);

/// <summary>
/// Read model for a named output file (e.g. report.csv) produced by a run step.
/// When <paramref name="IsBinary"/> is set, <paramref name="Content"/> is base64 of the file bytes.
/// </summary>
public sealed record RunArtifactView(string Name, string Content, bool IsBinary = false);

/// <summary>Snapshot of the workload spec that was executed — for run history fidelity.</summary>
public sealed record JobRunSnapshotView(
    /// <summary>"image" or "code"</summary>
    string MapSourceKind,
    string MapSourceLabel,
    string? ReduceSourceKind,
    string? ReduceSourceLabel,
    int ConcurrencyLimit,
    int ShardCount,
    /// <summary>Whether outbound network access was permitted for this run's containers.</summary>
    bool AllowNetworkEgress);

/// <summary>Full job run detail including all shard and reduce artifacts.</summary>
public sealed record JobRunDetailView(
    Guid Id,
    Guid JobId,
    Guid ProjectId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<ShardResultView> ShardResults,
    ReduceResultView? ReduceResult,
    JobRunSnapshotView Snapshot,
    /// <summary>1-based attempt number for this run. Retries increment this value.</summary>
    int AttemptNumber = 1,
    /// <summary>Id of the first run in this retry chain. Null for the first attempt.</summary>
    Guid? OriginalRunId = null);
