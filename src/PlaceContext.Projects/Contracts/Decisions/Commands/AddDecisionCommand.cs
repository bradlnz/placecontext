using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Record an architecture decision (ADR-lite) for a project.</summary>
public sealed record AddDecisionCommand(Guid ProjectId, string Question, string Choice, string? Rationale)
    : ICommand<DecisionView>;
