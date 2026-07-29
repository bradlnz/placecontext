using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfJobTriggerRepository : IJobTriggerRepository
{
    private readonly AppDbContext _db;

    public EfJobTriggerRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(JobTrigger trigger, CancellationToken ct = default)
        => await _db.JobTriggers.AddAsync(ToRow(trigger), ct);

    public async Task UpdateAsync(JobTrigger trigger, CancellationToken ct = default)
    {
        var existing = await _db.JobTriggers.FindAsync(new object[] { trigger.Id }, ct);
        if (existing is null) return;

        existing.Name = trigger.Name;
        existing.Enabled = trigger.Enabled;
        existing.CronExpression = trigger.CronExpression;
        existing.EventName = trigger.EventName;
        existing.NextRunAt = trigger.NextRunAt;
        existing.LastFiredAt = trigger.LastFiredAt;
        existing.UpdatedAt = trigger.UpdatedAt;
    }

    public async Task RemoveAsync(Guid triggerId, CancellationToken ct = default)
    {
        var existing = await _db.JobTriggers.FindAsync(new object[] { triggerId }, ct);
        if (existing is not null) _db.JobTriggers.Remove(existing);
    }

    public async Task<JobTrigger?> GetByIdAsync(Guid triggerId, CancellationToken ct = default)
    {
        var row = await _db.JobTriggers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == triggerId, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<JobTrigger>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var rows = await _db.JobTriggers.AsNoTracking()
            .Where(t => t.ProjectId == projectId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<JobTrigger>> ListForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var rows = await _db.JobTriggers.AsNoTracking()
            .Where(t => t.JobId == jobId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<JobTrigger>> ListDueSchedulesAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var rows = await _db.JobTriggers.AsNoTracking()
            .Where(t => t.Enabled && (t.Kind == "Schedule" || t.Kind == "Launchpad" || t.Kind == "Command")
                        && t.NextRunAt != null && t.NextRunAt <= now)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<JobTrigger>> ListForEventAsync(string eventName, CancellationToken ct = default)
    {
        var rows = await _db.JobTriggers.AsNoTracking()
            .Where(t => t.Enabled && t.Kind == "Event" && t.EventName != null
                        && t.EventName.ToLower() == eventName.ToLower())
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    private static JobTriggerRow ToRow(JobTrigger t) => new()
    {
        Id = t.Id,
        ProjectId = t.ProjectId,
        JobId = t.JobId,
        Name = t.Name,
        Kind = t.Kind.ToString(),
        Enabled = t.Enabled,
        CronExpression = t.CronExpression,
        EventName = t.EventName,
        ChainId = t.ChainId,
        SourceTable = t.SourceTable,
        Prompt = t.Prompt,
        CommandId = t.CommandId,
        NextRunAt = t.NextRunAt,
        LastFiredAt = t.LastFiredAt,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt,
    };

    private static JobTrigger ToDomain(JobTriggerRow r) => JobTrigger.Rehydrate(
        r.Id, r.ProjectId, r.JobId, r.Name,
        Enum.Parse<TriggerKind>(r.Kind), r.Enabled,
        r.CronExpression, r.EventName, r.ChainId, r.SourceTable, r.Prompt,
        r.CommandId, r.NextRunAt, r.LastFiredAt, r.CreatedAt, r.UpdatedAt);
}
