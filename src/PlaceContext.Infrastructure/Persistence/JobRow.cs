namespace PlaceContext.Infrastructure.Persistence;

/// <summary>
/// Flat EF Core row for a <see cref="PlaceContext.Domain.Entities.Job"/> definition.
/// WorkloadSource is persisted discriminated: a "kind" column + image-or-code columns.
/// </summary>
public sealed class JobRow : ITenantOwned
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

    public int ConcurrencyLimit { get; set; } = 1;

    /// <summary>JSON array of declared input parameters [{Name,Label,Required}]. Empty = no prompt.</summary>
    public string ParametersJson { get; set; } = "[]";

    /// <summary>JSON array of post-job action kinds, e.g. ["HtmlReport","Chart"]. Empty = none.</summary>
    public string PostJobActionsJson { get; set; } = "[]";

    // ── Network policy ────────────────────────────────────────────────────────────────────────────
    /// <summary>True when containers may make outbound network calls. Default false (network-none sandbox).</summary>
    public bool AllowNetworkEgress { get; set; }

    /// <summary>Per-container wall-clock timeout in seconds. Default 300 (5 minutes).</summary>
    public int TimeoutSeconds { get; set; } = 300;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
