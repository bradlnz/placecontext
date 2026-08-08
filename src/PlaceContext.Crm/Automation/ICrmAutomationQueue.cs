namespace PlaceContext.Crm.Automation;

public interface ICrmAutomationQueue
{
    Task<Guid> EnqueueAsync(QueuedCrmAutomation value, CancellationToken ct = default);
}
