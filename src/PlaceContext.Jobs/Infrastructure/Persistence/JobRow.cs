namespace PlaceContext.Jobs.Infrastructure.Persistence;

/// <summary>
/// Flat EF Core row for a <see cref="PlaceContext.Domain.Entities.Job"/> definition.
/// WorkloadSource is persisted discriminated: a "kind" column + image-or-code columns.
/// </summary>
public sealed class JobRow : IJobsTenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    // ── Map step ──────────────────────────────────────────────────────────────────────────────────
    /// <summary>"image" | "code"</summary>
    public string MapSourceKind { get; set; } = "image";
    /// <summary>Container image (ImageWorkload path).</summary>
    public string? MapImage { get; set; }
    /// <summary>Runtime identifier (CodeWorkload path).</summary>
    public string? MapRuntimeId { get; set; }
    /// <summary>Inline source code — legacy single-file column (CodeWorkload path). Superseded by MapFilesJson.</summary>
    public string? MapSource { get; set; }
    /// <summary>JSON array of {Path,Content} source files (CodeWorkload path). Null/empty falls back to MapSource.</summary>
    public string? MapFilesJson { get; set; }
    /// <summary>Entry-point path (CodeWorkload path; null = use runtime default).</summary>
    public string? MapEntrypoint { get; set; }
    /// <summary>JSON array of opaque payload strings (one per shard).</summary>
    public string InputPayloadsJson { get; set; } = "[]";
    /// <summary>JSON object of env key/value pairs for the map containers.</summary>
    public string MapEnvJson { get; set; } = "{}";

    // ── Reduce step (nullable = no reduce) ───────────────────────────────────────────────────────
    /// <summary>"image" | "code" | null</summary>
    public string? ReduceSourceKind { get; set; }
    public string? ReduceImage { get; set; }
    public string? ReduceRuntimeId { get; set; }
    public string? ReduceSource { get; set; }
    /// <summary>JSON array of {Path,Content} source files (reduce CodeWorkload path). Null/empty falls back to ReduceSource.</summary>
    public string? ReduceFilesJson { get; set; }
    public string? ReduceEntrypoint { get; set; }
    public string? ReduceEnvJson { get; set; }

    // ── Policy ────────────────────────────────────────────────────────────────────────────────────
    /// <summary>JSON array of int exit codes that map to Succeeded.</summary>
    public string SuccessCodesJson { get; set; } = "[0]";
    /// <summary>JSON array of int exit codes that map to Partial.</summary>
    public string PartialCodesJson { get; set; } = "[]";

    /// <summary>Default max concurrent map shards for a new/unspecified job. Raised from 1 → 4 so
    /// multi-shard jobs parallelize their shards by default (throughput fix); jobs with an explicit
    /// persisted value are unaffected — this only governs rows built without setting the property.</summary>
    public int ConcurrencyLimit { get; set; } = 4;

    /// <summary>JSON array of declared input parameters [{Name,Label,Required}]. Empty = no prompt.</summary>
    public string ParametersJson { get; set; } = "[]";

    /// <summary>JSON array of post-job action kinds, e.g. ["HtmlReport","Chart"]. Empty = none.</summary>
    public string PostJobActionsJson { get; set; } = "[]";

    /// <summary>Declared return type of the job's primary output ("Json", "Table", "Chart", …).
    /// Drives the mandatory per-run artifact.</summary>
    public string ReturnType { get; set; } = "Json";

    /// <summary>Expected /out file name for file return types (Pdf/Image/Video). Null = by extension.</summary>
    public string? ReturnFileName { get; set; }

    // ── Network policy ────────────────────────────────────────────────────────────────────────────
    /// <summary>True when containers may make outbound network calls. Default false (network-none sandbox).</summary>
    public bool AllowNetworkEgress { get; set; }

    /// <summary>True when this job is allowed to run through the API endpoint.</summary>
    public bool AllowApiInvocation { get; set; }

    /// <summary>Per-container wall-clock timeout in seconds. Default 1800 (30 minutes).</summary>
    public int TimeoutSeconds { get; set; } = 1800;

    /// <summary>Maximum number of automatic retry attempts when a run fails. Default 0 (no retries).</summary>
    public int RetryCount { get; set; }

    /// <summary>Fixed delay in seconds between automatic retry attempts. Default 0.</summary>
    public int RetryDelaySeconds { get; set; }

    /// <summary>JSON array of MCP connection GUIDs this job can access. Empty = none.</summary>
    public string McpConnectionIdsJson { get; set; } = "[]";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
