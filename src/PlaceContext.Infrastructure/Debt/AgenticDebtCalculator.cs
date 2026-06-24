using PlaceContext.Application.Ports;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Debt;

/// <summary>
/// Agentic-debt strategy: replays the recent change window through the pure
/// <see cref="AgenticDebtScorer"/>, supplying god nodes and the re-touch flag from the ledger.
/// </summary>
public sealed class AgenticDebtCalculator : IDebtCalculator
{
    private readonly AgenticDebtScorer _scorer;
    public AgenticDebtCalculator(AgenticDebtScorer scorer) => _scorer = scorer;

    public DebtKind Kind => DebtKind.Agentic;

    public IReadOnlyList<DebtSignal> Compute(DebtInputs inputs)
    {
        var signals = new List<DebtSignal>();
        var window = inputs.Ledger.RecentWindow(inputs.ReTouchWindow);

        foreach (var change in window)
        {
            var reTouched = inputs.Ledger.TouchesWithin(
                change.TouchedNodes, inputs.ReTouchWindow, change.Sequence);
            signals.AddRange(_scorer.Score(change, inputs.GodNodes, reTouched));
        }

        return signals;
    }
}
