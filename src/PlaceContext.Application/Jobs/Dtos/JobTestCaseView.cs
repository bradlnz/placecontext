namespace PlaceContext.Application.Dtos;

/// <summary>A persisted unit-test-style case for a job definition.</summary>
public sealed record JobTestCaseView(
    Guid Id,
    Guid ProjectId,
    Guid JobId,
    string JobName,
    string Name,
    string? InputPayload,
    JobTestAssertionType AssertionType,
    string? ExpectedValue,
    bool Enabled,
    string LastStatus,
    string? LastMessage,
    string? LastActualOutput,
    Guid? LastJobRunId,
    DateTimeOffset? LastRunAt,
    long? LastDurationMs,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? RuntimeId,
    string? Entrypoint,
    IReadOnlyList<CodeFileDto> CodeFiles,
    bool AllowNetworkEgress,
    IReadOnlyList<JobTestMethodResult>? MethodResults = null);
