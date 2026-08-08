namespace PlaceContext.Host.Controllers;

public sealed record DashboardChain(
    Guid Id,
    Guid ProjectId,
    string Name,
    int StageCount,
    int JobCount,
    IReadOnlyList<DashboardChainStep> PromptSteps);
