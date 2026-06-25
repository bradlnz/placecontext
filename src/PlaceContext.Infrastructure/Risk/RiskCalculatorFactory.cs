using PlaceContext.Application.Ports;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Risk;

/// <summary>Selects risk strategies by kind. Factory seam parallel to CodeRag's strategy factories.</summary>
public sealed class RiskCalculatorFactory : IRiskCalculatorFactory
{
    private readonly IReadOnlyList<IRiskCalculator> _calculators;

    public RiskCalculatorFactory(IEnumerable<IRiskCalculator> calculators)
        => _calculators = calculators.ToList();

    public IRiskCalculator For(RiskKind kind)
        => _calculators.FirstOrDefault(c => c.Kind == kind)
           ?? throw new InvalidOperationException($"No risk calculator for {kind}.");

    public IReadOnlyList<IRiskCalculator> All() => _calculators;
}
