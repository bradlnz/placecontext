namespace PlaceContext.Jobs.Contracts.Api;

public sealed record JobTestBlockResponse(
    Guid Id,
    Guid ProjectId,
    Guid JobId,
    string JobName,
    string Name,
    string? InputPayload,
    string AssertionType,
    string? ExpectedValue,
    bool Enabled,
    string LastStatus,
    string? LastMessage,
    string? LastActualOutput,
    long? LastDurationMs,
    string RuntimeId,
    string RuntimeLabel,
    string? Entrypoint,
    IReadOnlyList<JobTestCodeFileResponse> CodeFiles,
    IReadOnlyList<JobTestMethodResponse> MethodResults);
