using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Crm.Automation;

public sealed record QueuedCrmAutomation(
    Guid TenantId,
    Guid ProjectId,
    Guid RuleId,
    Guid? ClientId,
    Guid ChainId,
    CrmAutomationEventType EventType,
    CustomerLifecycleStage? LifecycleStage,
    string RuleName,
    string? InputPayload = null);
