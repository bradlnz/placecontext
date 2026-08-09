using PlaceContext.Domain.ValueObjects;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Operations.Contracts.Backup;

/// <summary>
/// A job definition. Mirrors <see cref="JobView"/>'s workload-source shape (image XOR runtime+files)
/// plus the fields backup fidelity needs that the UI read-model doesn't carry (<see cref="TimeoutSeconds"/>).
/// Natural key on import is (ProjectId, Name) within the resolved project.
/// </summary>
public sealed record JobManifest(
    Guid JobId,
    Guid ProjectId,
    string Name,
    string? Description,

    /// <summary>"image" or "code".</summary>
    string MapSourceKind,
    string? MapImage,
    string? MapRuntimeId,
    string? MapEntrypoint,
    IReadOnlyList<CodeFileDto> MapFiles,
    IReadOnlyList<string> InputPayloads,
    IReadOnlyDictionary<string, string> MapEnv,

    string? ReduceSourceKind,
    string? ReduceImage,
    string? ReduceRuntimeId,
    string? ReduceEntrypoint,
    IReadOnlyList<CodeFileDto> ReduceFiles,
    IReadOnlyDictionary<string, string>? ReduceEnv,

    int ConcurrencyLimit,
    IReadOnlyList<int> SuccessExitCodes,
    IReadOnlyList<int> PartialExitCodes,
    bool AllowNetworkEgress,
    bool AllowApiInvocation,
    int TimeoutSeconds,
    IReadOnlyList<JobParameterDto> Parameters,
    IReadOnlyList<PostJobActionKind> PostJobActions,
    JobReturnType ReturnType,
    string? ReturnFileName,
    int RetryCount = 0,
    int RetryDelaySeconds = 0);
