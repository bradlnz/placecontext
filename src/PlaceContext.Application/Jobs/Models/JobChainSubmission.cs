namespace PlaceContext.Application.Jobs;

/// <summary>
/// Durable acknowledgement for an asynchronous job-chain submission made through MCP. The tracking
/// id identifies the submission receipt; the pre-allocated chain-run id identifies the actual run as
/// soon as a worker starts it.
/// </summary>
public sealed record JobChainSubmission(
    Guid TrackingId,
    Guid ProjectId,
    Guid ChainId,
    Guid ChainRunId,
    string Status,
    int Attempts,
    string? Error,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt)
{
    public DateTimeOffset? AvailableAt
    {
        get; init;
    }
    public DateTimeOffset? ExpiresAt
    {
        get; init;
    }
    public IReadOnlyList<Guid> PreviousRunIds { get; init; } = [];
}
