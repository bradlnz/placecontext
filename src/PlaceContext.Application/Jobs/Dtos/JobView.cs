namespace PlaceContext.Application.Dtos;

/// <summary>Read model for a job definition — includes full workload source details for the editor.</summary>
public sealed record JobView(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,

    // ── Map step ─────────────────────────────────────────────────────────────────────────────────
    /// <summary>"image" or "code"</summary>
    string MapSourceKind,
    string? MapImage,
    string? MapRuntimeId,
    /// <summary>Entry-point file content (single-file convenience view). Null for image workloads.</summary>
    string? MapSource,
    string? MapEntrypoint,
    /// <summary>Full map-step source file set (code workloads). Empty for image workloads.</summary>
    IReadOnlyList<CodeFileDto> MapFiles,
    int ShardCount,
    IReadOnlyList<string> InputPayloads,
    IReadOnlyDictionary<string, string> MapEnv,

    // ── Reduce step (optional) ────────────────────────────────────────────────────────────────────
    string? ReduceSourceKind,
    string? ReduceImage,
    string? ReduceRuntimeId,
    string? ReduceSource,
    string? ReduceEntrypoint,
    /// <summary>Full reduce-step source file set (code workloads). Empty when no reduce or image workload.</summary>
    IReadOnlyList<CodeFileDto> ReduceFiles,
    IReadOnlyDictionary<string, string>? ReduceEnv,

    int ConcurrencyLimit,
    IReadOnlyList<int> SuccessExitCodes,
    IReadOnlyList<int> PartialExitCodes,
    /// <summary>True when containers for this job are permitted outbound network access.</summary>
    bool AllowNetworkEgress,
    /// <summary>Declared input parameters prompted before a manual run (empty = none).</summary>
    IReadOnlyList<JobParameterDto> Parameters,
    /// <summary>Post-job actions that produce stored outputs after each run (empty = none).</summary>
    IReadOnlyList<PlaceContext.Domain.ValueObjects.PostJobActionKind> PostJobActions,
    /// <summary>Declared type of the job's primary return — drives the mandatory per-run artifact.</summary>
    PlaceContext.Domain.ValueObjects.JobReturnType ReturnType,
    /// <summary>Expected /out file name for file return types.</summary>
    string? ReturnFileName,

    /// <summary>Maximum number of automatic retry attempts when a run fails. 0 = no retries.</summary>
    int RetryCount,

    /// <summary>Fixed delay in seconds between automatic retry attempts.</summary>
    int RetryDelaySeconds,

    /// <summary>MCP connection IDs the job can access. Empty = none.</summary>
    IReadOnlyList<Guid> McpConnectionIds,

    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
