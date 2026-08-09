namespace PlaceContext.App.Dashboard;

internal sealed record EntityChartResult(string Column, IReadOnlyList<DashboardEntityBar> Bars);
