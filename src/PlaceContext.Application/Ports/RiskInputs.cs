using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>Everything the risk strategies may read, gathered once by the recompute handler.</summary>
public sealed record RiskInputs(
    GraphMetrics Graph,
    CodeMetrics Code,
    IReadOnlyList<GodNode> GodNodes,
    ActivityLog Activity,
    int ReTouchWindow);
