using PlaceContext.Application.Ports;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Risk;

/// <summary>
/// Process-risk strategy: replays the recent change window through the pure
/// <see cref="ProcessRiskScorer"/>, supplying god nodes and the re-touch flag from the ledger.
/// </summary>
public sealed class ProcessRiskCalculator : IRiskCalculator
{
    private readonly ProcessRiskScorer _scorer;
    public ProcessRiskCalculator(ProcessRiskScorer scorer) => _scorer = scorer;

    public RiskKind Kind => RiskKind.Process;

    public IReadOnlyList<RiskSignal> Compute(RiskInputs inputs)
    {
        var signals = new List<RiskSignal>();
        var window = inputs.Activity.RecentWindow(inputs.ReTouchWindow);

        foreach (var change in window)
        {
            var reTouched = inputs.Activity.TouchesWithin(
                change.TouchedNodes, inputs.ReTouchWindow, change.Sequence);
            signals.AddRange(_scorer.Score(change, inputs.GodNodes, reTouched));
        }

        return signals;
    }
}
