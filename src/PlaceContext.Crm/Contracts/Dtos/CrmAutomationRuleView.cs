namespace PlaceContext.Application.Features;

public sealed record CrmAutomationRuleView(
    Guid Id,
    Guid ProjectId,
    string Name,
    string EventType,
    string? LifecycleStage,
    Guid ChainId,
    string ChainName,
    int ChainSteps,
    bool Enabled,
    DateTimeOffset UpdatedAt);
