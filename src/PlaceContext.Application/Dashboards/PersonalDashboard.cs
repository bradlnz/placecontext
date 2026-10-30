namespace PlaceContext.Application.Dashboards;

public sealed record PersonalDashboard(Guid Id, string Name, IReadOnlyList<Guid> ChartIds, string Question, Guid Version);
