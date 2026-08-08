using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Replace a chain's name/description/stages. <paramref name="StepJobIds"/> is the backward-compatible
/// flat form — one job id per (sequential) stage. Pass <paramref name="Stages"/> to replace it with a
/// fan-out/fan-in pipeline instead (wins over <paramref name="StepJobIds"/> when present).
/// <paramref name="StageGates"/> is an optional parallel list of gates, one per stage (null = no gate).
/// </summary>
public sealed record UpdateJobChainCommand(
    Guid ChainId, string Name, string? Description, IReadOnlyList<Guid> StepJobIds,
    IReadOnlyList<IReadOnlyList<Guid>>? Stages = null,
    IReadOnlyList<ChainGate?>? StageGates = null,
    IReadOnlyList<ChainAction?>? StageActions = null)
    : ICommand<JobChainView>;
