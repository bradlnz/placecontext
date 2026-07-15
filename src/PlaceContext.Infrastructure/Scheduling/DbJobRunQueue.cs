using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Scheduling;

/// <summary>
/// Durable <see cref="IJobRunQueue"/> backed by the <c>pending_job_runs</c> table. Scoped: the enqueue
/// is added to the caller's <see cref="AppDbContext"/> so it commits in the same transaction as the
/// trigger update that produced it. The background <see cref="TriggerSchedulerService"/> drains rows
/// with atomic claiming, so any k3s replica can process them and they survive restarts.
/// Payloads are encrypted at rest.
/// </summary>
public sealed class DbJobRunQueue : IJobRunQueue
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly IDataEncryptor _enc;
    private static string P => IDataEncryptor.Purpose.PendingRun;

    public DbJobRunQueue(AppDbContext db, IClock clock, IDataEncryptor enc)
    {
        _db = db;
        _clock = clock;
        _enc = enc;
    }

    public async Task EnqueueAsync(QueuedJobRun run, CancellationToken ct = default)
        => await _db.PendingRuns.AddAsync(new PendingRunRow
        {
            Id = Guid.NewGuid(),
            TenantId = run.TenantId,
            JobId = run.JobId,
            TriggerId = run.TriggerId,
            TriggerName = run.TriggerName,
            Payload = run.Payload is null ? null : _enc.Protect(run.Payload, P),
            EnqueuedAt = _clock.UtcNow,
        }, ct);
}
