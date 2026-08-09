namespace PlaceContext.App.Dashboard;

public sealed record DashboardResponse(DashboardProject? Project, DashboardStats Stats, IReadOnlyList<DashboardChain> Chains, IReadOnlyList<DashboardEntity> Entities, IReadOnlyList<DashboardChart> Charts, IReadOnlyList<DashboardRun> RecentRuns);
