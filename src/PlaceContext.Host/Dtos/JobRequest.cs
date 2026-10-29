using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Api;

/// <summary>
/// Request body for creating (POST) or replacing (PUT) a job definition. Mirrors
/// <see cref="CreateJobCommand"/>/<see cref="UpdateJobCommand"/>'s shape but is kept as its own type so
/// the management API's contract is stable even if the internal command evolves. Exactly one of MapImage
/// or (MapRuntimeId + MapSource/MapFiles) must be supplied for the map step; the same rule applies to the
/// optional reduce step (all Reduce* fields omitted = no reduce step).
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
    /// <summary>Max concurrent map shards. Default raised from 1 → 4 so multi-shard jobs parallelize
    /// their shards out of the box; callers that need strict serial shard execution still pass 1
    /// explicitly.</summary>
    int ConcurrencyLimit = 4,
    IReadOnlyCollection<int>? SuccessExitCodes = null,
    IReadOnlyCollection<int>? PartialExitCodes = null,
    /// <summary>Opt-in outbound network access for the job's containers. Default false (--network none).</summary>
    bool AllowNetworkEgress = false,
    /// <summary>Opt-in API invocation for this job. Default false.</summary>
    bool AllowApiInvocation = false,
    IReadOnlyList<JobParameterRequest>? Parameters = null,
    /// <summary>Each entry one of "HtmlReport" | "Chart" | "Csv" | "RawBundle" | "HtmlOutput".</summary>
    IReadOnlyList<string>? PostJobActions = null,
    /// <summary>"Json" | "Table" | "Chart" | "Html" | "Csv" | "Text" | "Pdf" | "Image" | "Video".</summary>
    string ReturnType = "Json",
    /// <summary>Expected /out file name for file return types (Pdf/Image/Video).</summary>
    string? ReturnFileName = null,
    /// <summary>Maximum number of automatic retry attempts when a run fails. 0 = no retries.</summary>
    int RetryCount = 0,
    /// <summary>Fixed delay in seconds between automatic retry attempts.</summary>
    int RetryDelaySeconds = 0,
    /// <summary>MCP connection IDs the job can access. Injected as env vars at runtime.</summary>
    IReadOnlyList<Guid>? McpConnectionIds = null);
