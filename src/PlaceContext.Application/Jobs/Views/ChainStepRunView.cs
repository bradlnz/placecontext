namespace PlaceContext.Application.Dtos;

/// <summary>One step of a chain run: Pending/Running/Succeeded/Partial/Failed/Skipped, and the
/// job run it produced once it started. JobName is a snapshot taken when the run began.
/// <paramref name="Index"/> is the step's flat position across every stage (0-based). <paramref
/// name="StageIndex"/> is which stage it belongs to; <paramref name="BranchIndex"/> is its 0-based
/// position within that stage — a fan-out group has more than one step sharing a StageIndex, each
/// with its own BranchIndex; every step of a linear chain has BranchIndex 0.</summary>
public sealed record ChainStepRunView(
    int Index,
    int StageIndex,
    int BranchIndex,
    Guid JobId,
    string JobName,
    Guid? RunId,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);
