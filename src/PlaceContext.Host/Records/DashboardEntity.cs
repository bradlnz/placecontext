namespace PlaceContext.Host.Controllers;

public sealed record DashboardEntity(
    Guid Id,
    Guid ProjectId,
    string Name,
    string TableName,
    long? RowCount,
    string? ChartColumn,
    IReadOnlyList<DashboardEntityBar> Bars);
