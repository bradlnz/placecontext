using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Jobs.Contracts.Management;

namespace PlaceContext.Jobs.Management;

/// <summary>Translates between public Jobs management contracts and internal commands/views.</summary>
public static class JobApiMapper
{
    public static CreateJobCommand ToCreateCommand(Guid projectId, JobRequest request) => new(
        projectId, request.Name, request.Description,
        request.MapImage, request.MapRuntimeId, request.MapSource, request.MapEntrypoint,
        request.InputPayloads ?? Array.Empty<string>(),
        request.MapEnv ?? new Dictionary<string, string>(),
        request.ReduceImage, request.ReduceRuntimeId, request.ReduceSource, request.ReduceEntrypoint,
        request.ReduceEnv, request.ConcurrencyLimit,
        request.SuccessExitCodes ?? new[] { 0 },
        request.PartialExitCodes ?? Array.Empty<int>(),
        request.AllowNetworkEgress, request.AllowApiInvocation,
        ToCodeFiles(request.MapFiles), ToCodeFiles(request.ReduceFiles),
        ToParameters(request.Parameters), ParsePostJobActions(request.PostJobActions),
        ParseReturnType(request.ReturnType), request.ReturnFileName,
        request.RetryCount, request.RetryDelaySeconds,
        McpConnectionIds: request.McpConnectionIds);

    public static UpdateJobCommand ToUpdateCommand(Guid jobId, JobRequest request) => new(
        jobId, request.Name, request.Description,
        request.MapImage, request.MapRuntimeId, request.MapSource, request.MapEntrypoint,
        request.InputPayloads ?? Array.Empty<string>(),
        request.MapEnv ?? new Dictionary<string, string>(),
        request.ReduceImage, request.ReduceRuntimeId, request.ReduceSource, request.ReduceEntrypoint,
        request.ReduceEnv, request.ConcurrencyLimit,
        request.SuccessExitCodes ?? new[] { 0 },
        request.PartialExitCodes ?? Array.Empty<int>(),
        request.AllowNetworkEgress, request.AllowApiInvocation,
        ToCodeFiles(request.MapFiles), ToCodeFiles(request.ReduceFiles),
        ToParameters(request.Parameters), ParsePostJobActions(request.PostJobActions),
        ParseReturnType(request.ReturnType), request.ReturnFileName,
        request.RetryCount, request.RetryDelaySeconds,
        McpConnectionIds: request.McpConnectionIds);

    public static JobResponse ToResponse(JobView view) => new(
        view.Id, view.ProjectId, view.Name, view.Description,
        view.MapSourceKind, view.MapImage, view.MapRuntimeId, view.MapSource, view.MapEntrypoint,
        view.MapFiles.Select(file => new JobCodeFile(file.Path, file.Content)).ToList(),
        view.InputPayloads, view.MapEnv,
        view.ReduceSourceKind, view.ReduceImage, view.ReduceRuntimeId, view.ReduceSource,
        view.ReduceEntrypoint,
        view.ReduceFiles.Select(file => new JobCodeFile(file.Path, file.Content)).ToList(),
        view.ReduceEnv, view.ConcurrencyLimit, view.SuccessExitCodes, view.PartialExitCodes,
        view.AllowNetworkEgress, view.AllowApiInvocation,
        view.Parameters.Select(parameter => new JobParameterRequest(
            parameter.Name, parameter.Label, parameter.Required, parameter.Type, parameter.Options)).ToList(),
        view.PostJobActions.Select(action => action.ToString()).ToList(),
        view.ReturnType.ToString(), view.ReturnFileName, view.RetryCount, view.RetryDelaySeconds,
        view.McpConnectionIds.ToList(), view.CreatedAt, view.UpdatedAt);

    private static IReadOnlyList<CodeFileDto>? ToCodeFiles(IReadOnlyList<JobCodeFile>? files)
        => files?.Select(file => new CodeFileDto(file.Path, file.Content)).ToList();

    private static IReadOnlyList<JobParameterDto>? ToParameters(
        IReadOnlyList<JobParameterRequest>? parameters)
        => parameters?.Select(parameter => new JobParameterDto(
            parameter.Name, parameter.Label, parameter.Required, parameter.Type, parameter.Options)).ToList();

    private static IReadOnlyList<PostJobActionKind>? ParsePostJobActions(
        IReadOnlyList<string>? actions)
        => actions?.Select(action => Enum.TryParse<PostJobActionKind>(action, true, out var kind)
            ? kind
            : throw new ArgumentException($"Unknown post-job action '{action}'.")).ToList();

    private static JobReturnType ParseReturnType(string returnType)
        => Enum.TryParse<JobReturnType>(returnType, true, out var type)
            ? type
            : throw new ArgumentException($"Unknown return type '{returnType}'.");
}
