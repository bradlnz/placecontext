using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

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

public interface ICrmAutomationQueue
{
    Task<Guid> EnqueueAsync(QueuedCrmAutomation value, CancellationToken ct = default);
}

public sealed record CrmAutomationReceipt(
    Guid TrackingId,
    Guid RuleId,
    Guid ChainId,
    string RuleName);
