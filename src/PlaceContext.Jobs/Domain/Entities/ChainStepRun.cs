using PlaceContext.Domain.Common;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>One step of a chain run: which job, which stage/branch position it occupies, the run it
/// produced (once started), and where it is in its lifecycle. JobName is snapshotted at start.
/// <paramref name="Index"/> is the step's flat position across every stage (0-based, stage order
/// then branch order) — the same addressing scheme <see cref="ChainRun.MarkStepRunning"/> and
/// <see cref="ChainRun.MarkStepFinished"/> use. <paramref name="StageIndex"/> is which stage the
/// step belongs to; <paramref name="BranchIndex"/> is its 0-based position within that stage (always
/// 0 for a size-1 stage — i.e. every step of a linear chain).</summary>
public sealed record ChainStepRun(
    int Index,
    int StageIndex,
    int BranchIndex,
    Guid JobId,
    string JobName,
    Guid? RunId,
    ChainStepStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? ActionType = null,
    string? Provider = null,
    string? ExternalId = null,
    string? Error = null);
