using PlaceContext.Application.Features;

namespace PlaceContext.Application.Dashboards;

public sealed record DashboardChart(Guid Id, string Name, ChartSpec Spec, DateTimeOffset GeneratedAt);
