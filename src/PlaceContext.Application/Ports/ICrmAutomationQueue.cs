using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

public interface ICrmAutomationQueue
{
    Task<Guid> EnqueueAsync(QueuedCrmAutomation value, CancellationToken ct = default);
}
