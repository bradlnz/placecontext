using PlaceContext.Application.Ports;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Debt;

/// <summary>Technical-debt strategy: delegates to the pure <see cref="TechnicalDebtScorer"/>.</summary>
public sealed class TechnicalDebtCalculator : IDebtCalculator
{
    private readonly TechnicalDebtScorer _scorer;
    public TechnicalDebtCalculator(TechnicalDebtScorer scorer) => _scorer = scorer;

    public DebtKind Kind => DebtKind.Technical;

    public IReadOnlyList<DebtSignal> Compute(DebtInputs inputs)
        => _scorer.Score(inputs.Graph, inputs.Code);
}
