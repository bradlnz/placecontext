namespace PlaceContext.Jobs.Contracts.Management;

/// <summary>
/// Request body for creating or replacing a job definition. Exactly one of MapImage or
/// (MapRuntimeId + MapSource/MapFiles) must be supplied for the map step; the same rule applies to
/// the optional reduce step.
/// </summary>
public sealed record JobRequest(
    string Name,
    string? Description,
    string? MapImage,
    string? MapRuntimeId,
    string? MapSource,
    string? MapEntrypoint,
    IReadOnlyList<JobCodeFile>? MapFiles,
    IReadOnlyList<string>? InputPayloads,
    IReadOnlyDictionary<string, string>? MapEnv,
    string? ReduceImage,
    string? ReduceRuntimeId,
    string? ReduceSource,
    string? ReduceEntrypoint,
    IReadOnlyList<JobCodeFile>? ReduceFiles,
    IReadOnlyDictionary<string, string>? ReduceEnv,
    int ConcurrencyLimit = 4,
    IReadOnlyCollection<int>? SuccessExitCodes = null,
    IReadOnlyCollection<int>? PartialExitCodes = null,
    bool AllowNetworkEgress = false,
    bool AllowApiInvocation = false,
    IReadOnlyList<JobParameterRequest>? Parameters = null,
    IReadOnlyList<string>? PostJobActions = null,
    string ReturnType = "Json",
    string? ReturnFileName = null,
    int RetryCount = 0,
    int RetryDelaySeconds = 0,
    IReadOnlyList<Guid>? McpConnectionIds = null);
