using PlaceContext.Application.Ports;
using PlaceContext.Crm.Infrastructure.Persistence;

namespace PlaceContext.Crm.Infrastructure.Scheduling;

public sealed class DbCrmAutomationQueue : ICrmAutomationQueue
{
    private readonly CrmDbContext _db;
    private readonly IClock _clock;
    private readonly IDataEncryptor _encryptor;

    public DbCrmAutomationQueue(CrmDbContext db, IClock clock, IDataEncryptor encryptor)
        => (_db, _clock, _encryptor) = (db, clock, encryptor);

    public async Task<Guid> EnqueueAsync(QueuedCrmAutomation value, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var trackingId = Guid.NewGuid();
        await _db.CrmAutomationQueue.AddAsync(new CrmAutomationQueueRow
        {
            Id = trackingId,
            TenantId = value.TenantId,
            ProjectId = value.ProjectId,
            RuleId = value.RuleId,
            ClientId = value.ClientId,
            ChainId = value.ChainId,
            EventType = value.EventType.ToString(),
            LifecycleStage = value.LifecycleStage?.ToString(),
            RuleName = value.RuleName,
            InputPayloadProtected = _encryptor.Protect(
                value.InputPayload, DataEncryptionPurpose.CrmAutomationPayload),
            EnqueuedAt = now,
            NextAttemptAt = now,
        }, ct);
        return trackingId;
    }
}
