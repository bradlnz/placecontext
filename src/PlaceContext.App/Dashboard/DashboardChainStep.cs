namespace PlaceContext.App.Dashboard;

public sealed record DashboardChainStep(int Index, string JobName, IReadOnlyList<DashboardParameter> Parameters);
