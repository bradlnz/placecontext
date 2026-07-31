using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Api;

/// <summary>One source file in a code workload (map or reduce step).</summary>
public sealed record JobCodeFile(string Path, string Content);

/// <summary>A declared input field a job needs before it runs (prompted in a modal, or injected by an
/// event source).</summary>
public sealed record JobParameterRequest(
    string Name,
    string? Label = null,
    bool Required = true,
    /// <summary>"text" | "number" | "select" | "checkbox" | "file".</summary>
    string Type = "text",
    IReadOnlyList<string>? Options = null);

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

/// <summary>Public read model for a job definition, including its full workload source.</summary>
public sealed record JobResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    /// <summary>"image" or "code".</summary>
    string MapSourceKind,
    string? MapImage,
    string? MapRuntimeId,
    string? MapSource,
    string? MapEntrypoint,
    IReadOnlyList<JobCodeFile> MapFiles,
    IReadOnlyList<string> InputPayloads,
    IReadOnlyDictionary<string, string> MapEnv,
    string? ReduceSourceKind,
    string? ReduceImage,
    string? ReduceRuntimeId,
    string? ReduceSource,
    string? ReduceEntrypoint,
    IReadOnlyList<JobCodeFile> ReduceFiles,
    IReadOnlyDictionary<string, string>? ReduceEnv,
    int ConcurrencyLimit,
    IReadOnlyList<int> SuccessExitCodes,
    IReadOnlyList<int> PartialExitCodes,
    bool AllowNetworkEgress,
    IReadOnlyList<JobParameterRequest> Parameters,
    IReadOnlyList<string> PostJobActions,
    string ReturnType,
    string? ReturnFileName,
    int RetryCount,
    int RetryDelaySeconds,
    IReadOnlyList<Guid> McpConnectionIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Translates between the management API's job DTOs and the internal command/view types. Throws
/// <see cref="ArgumentException"/> on an unknown enum-string field — the controller turns that into 400.</summary>
internal static class JobApiMapper
{
    public static CreateJobCommand ToCreateCommand(Guid projectId, JobRequest r) => new(
        projectId, r.Name, r.Description,
        r.MapImage, r.MapRuntimeId, r.MapSource, r.MapEntrypoint,
        r.InputPayloads ?? Array.Empty<string>(),
        r.MapEnv ?? new Dictionary<string, string>(),
        r.ReduceImage, r.ReduceRuntimeId, r.ReduceSource, r.ReduceEntrypoint, r.ReduceEnv,
        r.ConcurrencyLimit,
        r.SuccessExitCodes ?? new[] { 0 },
        r.PartialExitCodes ?? Array.Empty<int>(),
        r.AllowNetworkEgress,
        ToCodeFiles(r.MapFiles),
        ToCodeFiles(r.ReduceFiles),
        ToParameters(r.Parameters),
        ParsePostJobActions(r.PostJobActions),
        ParseReturnType(r.ReturnType),
        r.ReturnFileName,
        r.RetryCount,
        r.RetryDelaySeconds,
        McpConnectionIds: r.McpConnectionIds);

    public static UpdateJobCommand ToUpdateCommand(Guid jobId, JobRequest r) => new(
        jobId, r.Name, r.Description,
        r.MapImage, r.MapRuntimeId, r.MapSource, r.MapEntrypoint,
        r.InputPayloads ?? Array.Empty<string>(),
        r.MapEnv ?? new Dictionary<string, string>(),
        r.ReduceImage, r.ReduceRuntimeId, r.ReduceSource, r.ReduceEntrypoint, r.ReduceEnv,
        r.ConcurrencyLimit,
        r.SuccessExitCodes ?? new[] { 0 },
        r.PartialExitCodes ?? Array.Empty<int>(),
        r.AllowNetworkEgress,
        ToCodeFiles(r.MapFiles),
        ToCodeFiles(r.ReduceFiles),
        ToParameters(r.Parameters),
        ParsePostJobActions(r.PostJobActions),
        ParseReturnType(r.ReturnType),
        r.ReturnFileName,
        r.RetryCount,
        r.RetryDelaySeconds,
        McpConnectionIds: r.McpConnectionIds);

    public static JobResponse ToResponse(JobView v) => new(
        v.Id, v.ProjectId, v.Name, v.Description,
        v.MapSourceKind, v.MapImage, v.MapRuntimeId, v.MapSource, v.MapEntrypoint,
        v.MapFiles.Select(f => new JobCodeFile(f.Path, f.Content)).ToList(),
        v.InputPayloads, v.MapEnv,
        v.ReduceSourceKind, v.ReduceImage, v.ReduceRuntimeId, v.ReduceSource, v.ReduceEntrypoint,
        v.ReduceFiles.Select(f => new JobCodeFile(f.Path, f.Content)).ToList(),
        v.ReduceEnv,
        v.ConcurrencyLimit, v.SuccessExitCodes, v.PartialExitCodes, v.AllowNetworkEgress,
        v.Parameters.Select(p => new JobParameterRequest(p.Name, p.Label, p.Required, p.Type, p.Options)).ToList(),
        v.PostJobActions.Select(a => a.ToString()).ToList(),
        v.ReturnType.ToString(),
        v.ReturnFileName,
        v.RetryCount,
        v.RetryDelaySeconds,
        v.McpConnectionIds.ToList(),
        v.CreatedAt, v.UpdatedAt);

    private static IReadOnlyList<CodeFileDto>? ToCodeFiles(IReadOnlyList<JobCodeFile>? files)
        => files?.Select(f => new CodeFileDto(f.Path, f.Content)).ToList();

    private static IReadOnlyList<JobParameterDto>? ToParameters(IReadOnlyList<JobParameterRequest>? parameters)
        => parameters?.Select(p => new JobParameterDto(p.Name, p.Label, p.Required, p.Type, p.Options)).ToList();

    private static IReadOnlyList<PostJobActionKind>? ParsePostJobActions(IReadOnlyList<string>? actions)
        => actions?.Select(a => Enum.TryParse<PostJobActionKind>(a, ignoreCase: true, out var k)
            ? k
            : throw new ArgumentException($"Unknown post-job action '{a}'.")).ToList();

    private static JobReturnType ParseReturnType(string returnType)
        => Enum.TryParse<JobReturnType>(returnType, ignoreCase: true, out var t)
            ? t
            : throw new ArgumentException($"Unknown return type '{returnType}'.");
}
