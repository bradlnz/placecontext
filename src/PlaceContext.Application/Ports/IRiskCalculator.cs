using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>Strategy that computes a set of risk signals; selected by <see cref="IRiskCalculatorFactory"/>.</summary>
public interface IRiskCalculator
{
    RiskKind Kind { get; }
    IReadOnlyList<RiskSignal> Compute(RiskInputs inputs);
}
