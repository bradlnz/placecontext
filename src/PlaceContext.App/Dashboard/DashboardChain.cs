namespace PlaceContext.App.Dashboard;

public sealed record DashboardChain(Guid Id, Guid ProjectId, string Name, int StageCount, int JobCount, IReadOnlyList<DashboardChainStep> PromptSteps);
