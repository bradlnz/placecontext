using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Scheduling;

public sealed class DbCrmAutomationQueue : ICrmAutomationQueue
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;

    public DbCrmAutomationQueue(AppDbContext db, IClock clock) => (_db, _clock) = (db, clock);

    public async Task EnqueueAsync(QueuedCrmAutomation value, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        await _db.CrmAutomationQueue.AddAsync(new CrmAutomationQueueRow
        {
            Id = Guid.NewGuid(),
            TenantId = value.TenantId,
            RuleId = value.RuleId,
            ClientId = value.ClientId,
            ChainId = value.ChainId,
            EventType = value.EventType.ToString(),
            LifecycleStage = value.LifecycleStage.ToString(),
            RuleName = value.RuleName,
            EnqueuedAt = now,
            NextAttemptAt = now,
        }, ct);
    }
}
