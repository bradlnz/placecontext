using System.Collections.Concurrent;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

/// <summary>Risk-strategy factory that contributes the supplied calculators (none by default).</summary>
public sealed class FakeRiskCalculatorFactory : IRiskCalculatorFactory
{
    private readonly List<IRiskCalculator> _calculators;
    public FakeRiskCalculatorFactory(params IRiskCalculator[] calculators) => _calculators = calculators.ToList();

    public IReadOnlyList<IRiskCalculator> All() => _calculators;
    public IRiskCalculator For(RiskKind kind) => _calculators.First(c => c.Kind == kind);
}
