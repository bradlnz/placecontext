namespace PlaceContext.Application.Dashboards;

/// <summary>A bounded local review proposal; it is not a model decision until explicitly evaluated.</summary>
public sealed record DashboardSignal(string Id, Guid ChartId, string ChartName, string? Source,
    string SeriesName, DashboardMeasurement? Measurement, DashboardConfidence DataConfidence,
    DashboardFreshness Freshness, bool NeedsData, string Proposal, string Rationale, string Question,
    DashboardWatchTarget? Target = null, double? ObservedTargetValue = null, bool? TargetBreached = null,
    double TargetTolerance = 0);
