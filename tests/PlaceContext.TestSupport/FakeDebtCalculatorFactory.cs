using System.Collections.Concurrent;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

/// <summary>Debt-strategy factory that contributes the supplied calculators (none by default).</summary>
public sealed class FakeDebtCalculatorFactory : IDebtCalculatorFactory
{
    private readonly List<IDebtCalculator> _calculators;
    public FakeDebtCalculatorFactory(params IDebtCalculator[] calculators) => _calculators = calculators.ToList();

    public IReadOnlyList<IDebtCalculator> All() => _calculators;
    public IDebtCalculator For(DebtKind kind) => _calculators.First(c => c.Kind == kind);
}
