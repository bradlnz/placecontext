namespace PlaceContext.Application.Dtos;

/// <summary>One stage of a chain run: Pending/Running/Succeeded/Partial/Failed/Skipped, and the
/// job run it produced once it started. JobName is a snapshot taken when the run began.</summary>
public sealed record ChainStepRunView(
    int Index,
    Guid JobId,
    string JobName,
    Guid? RunId,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);
