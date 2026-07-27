namespace PlaceContext.Application.Dtos;

/// <summary>Read model: the root statistics strip on the Overview.</summary>
public sealed record RootStatsView(
    int ProjectCount,
    int ChangesToday,
    int AgentChangesToday,
    int HumanChangesToday,
    int GodNodeTotal,
    int StaleContextCount);
