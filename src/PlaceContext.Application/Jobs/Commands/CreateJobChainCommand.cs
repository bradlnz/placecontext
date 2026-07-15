using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Define a chain: the named, ordered pipeline of jobs to run. <paramref name="StepJobIds"/> is the
/// backward-compatible flat form — one job id per (sequential) stage. To author a fan-out/fan-in
/// chain, pass <paramref name="Stages"/> instead: an ordered list of stages, each a list of job ids
/// that run in parallel when there is more than one (the stage right after a fan-out group is its
/// join). When <paramref name="Stages"/> is supplied it wins; <paramref name="StepJobIds"/> is
/// otherwise required so existing callers are unaffected.
/// </summary>
public sealed record CreateJobChainCommand(
    Guid ProjectId, string Name, string? Description, IReadOnlyList<Guid> StepJobIds,
    IReadOnlyList<IReadOnlyList<Guid>>? Stages = null)
    : ICommand<JobChainView>;
