namespace PlaceContext.Application.Dtos;

/// <summary>Read model: the outcome of onboarding a repository into PlaceContext.</summary>
public sealed record OnboardResultView(
    ProjectSummaryView Project,
    int ChangesBackfilled,
    bool ContextSeeded,
    IReadOnlyList<string> SkillsCreated,
    IReadOnlyList<string> AgentsCreated,
    IReadOnlyList<string> AvailablePrompts);
