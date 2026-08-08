namespace PlaceContext.Host.Controllers;

internal sealed record EntityChartResult(
    string Column,
    IReadOnlyList<DashboardEntityBar> Bars);
