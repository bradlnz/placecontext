using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Scheduling;

public sealed class DbCrmAutomationQueue : ICrmAutomationQueue
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly IDataEncryptor _encryptor;

    public DbCrmAutomationQueue(AppDbContext db, IClock clock, IDataEncryptor encryptor)
        => (_db, _clock, _encryptor) = (db, clock, encryptor);

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
            LifecycleStage = value.LifecycleStage?.ToString(),
            RuleName = value.RuleName,
            InputPayloadProtected = _encryptor.Protect(
                value.InputPayload, IDataEncryptor.Purpose.CrmAutomationPayload),
            EnqueuedAt = now,
            NextAttemptAt = now,
        }, ct);
    }
}
