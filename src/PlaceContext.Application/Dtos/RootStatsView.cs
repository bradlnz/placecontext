namespace PlaceContext.Application.Dtos;

/// <summary>Read model: the 6-up root statistics strip on the Overview.</summary>
public sealed record RootStatsView(
    int ProjectCount,
    int ChangesToday,
    int AgentChangesToday,
    int HumanChangesToday,
    double RootAgenticDebt,
    string RootAgenticBand,
    double RootTechnicalDebt,
    string RootTechnicalBand,
    int GodNodeTotal,
    int StaleContextCount);
