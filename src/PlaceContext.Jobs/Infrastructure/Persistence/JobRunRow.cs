namespace PlaceContext.Jobs.Infrastructure.Persistence;

/// <summary>
/// Flat EF Core row for a <see cref="PlaceContext.Domain.Entities.JobRun"/>.
/// ShardResults and ReduceResult are persisted as JSON columns (opaque JSON blobs).
/// The WorkloadSnapshot is persisted as a JSON column for full run-history fidelity.
/// </summary>
public sealed class JobRunRow : IJobsTenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid JobId { get; set; }
    public Guid ProjectId { get; set; }
    public string Status { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Denormalized shard counts — populated at write time so list queries never load ShardResultsJson.</summary>
    public int ShardCount { get; set; }
    public int SucceededShards { get; set; }
    public int PartialShards { get; set; }
    public int FailedShards { get; set; }

    /// <summary>JSON array of ShardResultJson objects.</summary>
    public string ShardResultsJson { get; set; } = "[]";

    /// <summary>JSON object for ReduceResult; null when no reduce step ran.</summary>
    public string? ReduceResultJson { get; set; }

    /// <summary>
    /// JSON-serialised WorkloadSnapshot (map+reduce source, payloads, env, concurrency).
    /// Guarantees run-history reproducibility regardless of later job edits.
    /// </summary>
    public string SnapshotJson { get; set; } = "{}";

    /// <summary>1-based attempt number for this run. Retries increment this value.</summary>
    public int AttemptNumber { get; set; } = 1;

    /// <summary>Id of the first run in this retry chain. Null for the first attempt.</summary>
    public Guid? OriginalRunId { get; set; }
}
