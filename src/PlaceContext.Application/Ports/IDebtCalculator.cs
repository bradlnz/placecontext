using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>Strategy that computes a set of debt signals; selected by <see cref="IDebtCalculatorFactory"/>.</summary>
public interface IDebtCalculator
{
    DebtKind Kind { get; }
    IReadOnlyList<DebtSignal> Compute(DebtInputs inputs);
}
