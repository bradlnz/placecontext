using PlaceContext.Application.Ports;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Risk;

/// <summary>Technical-risk strategy: delegates to the pure <see cref="TechnicalRiskScorer"/>.</summary>
public sealed class TechnicalRiskCalculator : IRiskCalculator
{
    private readonly TechnicalRiskScorer _scorer;
    public TechnicalRiskCalculator(TechnicalRiskScorer scorer) => _scorer = scorer;

    public RiskKind Kind => RiskKind.Technical;

    public IReadOnlyList<RiskSignal> Compute(RiskInputs inputs)
        => _scorer.Score(inputs.Graph, inputs.Code);
}
