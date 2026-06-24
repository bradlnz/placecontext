namespace PlaceContext.Application.Dtos;

/// <summary>Read model: the Debt &amp; Trust page rollup across the whole root.</summary>
public sealed record RootDebtView(
    double Agentic,
    string AgenticBand,
    double Technical,
    string TechnicalBand,
    int AgentChangesScored,
    int FlaggedChanges,
    IReadOnlyList<TrustSignalBar> TrustSignals,
    IReadOnlyList<TechMetricCard> TechMetrics,
    int StaleContextCount);
