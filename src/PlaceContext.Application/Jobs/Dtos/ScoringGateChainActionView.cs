using PlaceContext.Domain.ValueObjects;
namespace PlaceContext.Application.Dtos;

public sealed record ScoringGateChainActionView(string InputField, string OutputField, IReadOnlyList<ScoreCriterion> Criteria,
    double PassScore = 90, double MinimumConfidence = 0.8, bool StopOnFailure = true)
    : ChainActionView(ScoringGateChainAction.ActionType, "Scoring gate");
