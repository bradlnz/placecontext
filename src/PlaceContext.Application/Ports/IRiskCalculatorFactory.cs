using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>Selects risk strategies by kind.</summary>
public interface IRiskCalculatorFactory
{
    IRiskCalculator For(RiskKind kind);
    IReadOnlyList<IRiskCalculator> All();
}
