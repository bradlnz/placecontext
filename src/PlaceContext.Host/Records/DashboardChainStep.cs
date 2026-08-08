namespace PlaceContext.Host.Controllers;

public sealed record DashboardChainStep(
    int Index,
    string JobName,
    IReadOnlyList<DashboardParameter> Parameters);
