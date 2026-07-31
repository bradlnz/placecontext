namespace PlaceContext.Application.Dtos;

/// <summary>The assertion applied to a job's primary output after a successful run.</summary>
public enum JobTestAssertionType
{
    Succeeds,
    OutputEquals,
    OutputContains,
    JsonSubset,
}

/// <summary>The latest framework result for one method inside a test block.</summary>
public sealed record JobTestMethodResult(
    string Name,
    string Status,
    long? DurationMs = null,
    string? Message = null);

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
