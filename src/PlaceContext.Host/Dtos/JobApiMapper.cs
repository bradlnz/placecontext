using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Api;

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
        r.AllowApiInvocation,
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
        r.AllowApiInvocation,
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
        v.ConcurrencyLimit, v.SuccessExitCodes, v.PartialExitCodes, v.AllowNetworkEgress, v.AllowApiInvocation,
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
