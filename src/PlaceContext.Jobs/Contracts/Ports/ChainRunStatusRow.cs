using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>Compact status projection of one chain run, with just enough step detail to
/// describe progress and to identify the job runs the chain owns.</summary>
public sealed record ChainRunStatusRow(
    Guid RunId,
    Guid ChainId,
    string ChainName,
    Guid ProjectId,
    ChainRunStatus Status,
    int TotalSteps,
    int FinishedSteps,
    string? CurrentStepName,
    IReadOnlyList<Guid> StepRunIds,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);
