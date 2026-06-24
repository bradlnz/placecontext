using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>Selects debt strategies by kind.</summary>
public interface IDebtCalculatorFactory
{
    IDebtCalculator For(DebtKind kind);
    IReadOnlyList<IDebtCalculator> All();
}
