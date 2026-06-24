using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>Everything the debt strategies may read, gathered once by the recompute handler.</summary>
public sealed record DebtInputs(
    GraphMetrics Graph,
    CodeMetrics Code,
    IReadOnlyList<GodNode> GodNodes,
    ChangeLedger Ledger,
    int ReTouchWindow);
