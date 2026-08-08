using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Ports;

public sealed record JobTestCaseRecord(
    Guid Id,
    Guid ProjectId,
    Guid JobId,
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
