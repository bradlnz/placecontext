namespace PlaceContext.Application.Dtos;

/// <summary>Read model: the Risk &amp; Trust page rollup across the whole root.</summary>
public sealed record RootRiskView(
    double Process,
    string ProcessBand,
    double Technical,
    string TechnicalBand,
    int AgentChangesScored,
    int FlaggedChanges,
    IReadOnlyList<TrustSignalBar> TrustSignals,
    IReadOnlyList<TechMetricCard> TechMetrics,
    int StaleContextCount);
