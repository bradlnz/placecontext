namespace PlaceContext.Application.Build;

public sealed record DashboardChart(string ChartJson, string Source)
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public bool Live { get; init; }
    public string? Sql { get; init; }
}
