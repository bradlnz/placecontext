namespace PlaceContext.Application.Dtos;

/// <summary>
/// A chain run as a staged pipeline: one entry per step with its live status (steps after a
/// failure show Skipped), plus the final payload — the last step's primary output. Persisted from
/// the moment the run starts, so the portal can watch the stages progress and keep a history.
/// </summary>
public sealed record ChainRunView(
    Guid Id,
    Guid ChainId,
    string ChainName,
    string Status,
    IReadOnlyList<ChainStepRunView> Steps,
    string? FinalOutput,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);
