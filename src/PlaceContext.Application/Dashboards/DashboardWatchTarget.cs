namespace PlaceContext.Application.Dashboards;

public enum DashboardMeasureStatistic { Minimum, Maximum, Mean, Median }
public enum DashboardTargetDirection { AtMost, AtLeast }

/// <summary>A user-defined bound in the existing chart's units; no unit or business meaning is inferred.</summary>
public sealed record DashboardWatchTarget(Guid ChartId, string SeriesName,
    DashboardMeasureStatistic Statistic, DashboardTargetDirection Direction, double Threshold);
