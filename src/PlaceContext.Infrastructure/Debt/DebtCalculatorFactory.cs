using PlaceContext.Application.Ports;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Debt;

/// <summary>Selects debt strategies by kind. Factory seam parallel to CodeRag's strategy factories.</summary>
public sealed class DebtCalculatorFactory : IDebtCalculatorFactory
{
    private readonly IReadOnlyList<IDebtCalculator> _calculators;

    public DebtCalculatorFactory(IEnumerable<IDebtCalculator> calculators)
        => _calculators = calculators.ToList();

    public IDebtCalculator For(DebtKind kind)
        => _calculators.FirstOrDefault(c => c.Kind == kind)
           ?? throw new InvalidOperationException($"No debt calculator for {kind}.");

    public IReadOnlyList<IDebtCalculator> All() => _calculators;
}
