namespace PlaceContext.Host.Api;

/// <summary>Shared request type for creating or provisioning a tenant workspace.</summary>
public sealed record CoreCreateProjectRequest(string Path, string? Name);

/// <summary>Health-safe project response for frontend clients.</summary>
public sealed record CoreProjectResponse(
    Guid Id,
    string Name,
    string Path,
    string Status,
    bool IsGraphified);

/// <summary>Public job contract for frontend client UIs.</summary>
public sealed record CoreJobResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    string MapSourceKind,
    string? MapImage,
    string? MapRuntimeId,
    string? MapSource,
    string? MapEntrypoint,
    IReadOnlyList<CoreCodeFile> MapFiles,
    IReadOnlyList<string> InputPayloads,
    IReadOnlyDictionary<string, string> MapEnv,
    string? ReduceSourceKind,
    string? ReduceImage,
    string? ReduceRuntimeId,
    string? ReduceSource,
    string? ReduceEntrypoint,
    IReadOnlyList<CoreCodeFile> ReduceFiles,
    IReadOnlyDictionary<string, string>? ReduceEnv,
    int ConcurrencyLimit,
    IReadOnlyList<int> SuccessExitCodes,
    IReadOnlyList<int> PartialExitCodes,
    bool AllowNetworkEgress,
    bool AllowApiInvocation,
    IReadOnlyList<CoreJobParameter> Parameters,
    IReadOnlyList<string> PostJobActions,
    string ReturnType,
    string? ReturnFileName,
    int RetryCount,
    int RetryDelaySeconds,
    IReadOnlyList<Guid> McpConnectionIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CoreJobSummaryResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    string MapSourceKind,
    string ReturnType,
    bool AllowApiInvocation,
    bool AllowNetworkEgress,
    DateTimeOffset UpdatedAt);

public sealed record CoreCodeFile(string Path, string Content);

public sealed record CoreJobParameter(
    string Name,
    string? Label,
    bool Required,
    string Type,
    IReadOnlyList<string>? Options);

public sealed record CoreRunJobRequest(
    string? InputPayload = null,
    Guid? RunId = null);

public sealed record CoreJobRunSummaryResponse(
    Guid Id,
    Guid JobId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int ShardCount,
    int SucceededShards,
    int PartialShards,
    int FailedShards);

public sealed record CoreJobRunDetailResponse(
    Guid Id,
    Guid JobId,
    Guid ProjectId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int AttemptNumber,
    Guid? OriginalRunId,
    CoreRunSnapshotResponse Snapshot,
    IReadOnlyList<CoreShardResult> Shards,
    CoreReduceResult? ReduceResult);

public sealed record CoreRunSnapshotResponse(
    string MapSourceKind,
    string MapSourceLabel,
    string? ReduceSourceKind,
    string? ReduceSourceLabel,
    int ConcurrencyLimit,
    int ShardCount,
    bool AllowNetworkEgress);

public sealed record CoreShardResult(
    int Index,
    int ExitCode,
    string Outcome,
    string? Artifact,
    string? Log,
    IReadOnlyList<CoreRunArtifact> Artifacts);

public sealed record CoreReduceResult(
    int ExitCode,
    bool Succeeded,
    string? Artifact,
    string? Log,
    IReadOnlyList<CoreRunArtifact> Artifacts);

public sealed record CoreRunArtifact(string Name, string Content, bool IsBinary = false);
