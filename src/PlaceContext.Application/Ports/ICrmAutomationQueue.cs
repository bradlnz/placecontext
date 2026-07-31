using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

public sealed record QueuedCrmAutomation(
    Guid TenantId,
    Guid RuleId,
    Guid ClientId,
    Guid ChainId,
    CrmAutomationEventType EventType,
    CustomerLifecycleStage LifecycleStage,
    string RuleName);

public interface ICrmAutomationQueue
{
    Task EnqueueAsync(QueuedCrmAutomation value, CancellationToken ct = default);
}
