using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfEntityTagStore : IEntityTagStore
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;

    public EfEntityTagStore(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task AddAsync(IReadOnlyList<EntityTag> tags, CancellationToken ct = default)
    {
        if (tags.Count == 0) return;
        // Skip duplicates for (entity, key, run) — the tag pass may re-run on retried runs.
        var runIds = tags.Select(t => t.RunId).Distinct().ToList();
        var existing = await _db.EntityTags.AsNoTracking()
            .Where(r => runIds.Contains(r.RunId))
            .Select(r => new { r.EntityId, r.Key, r.RunId })
            .ToListAsync(ct);
        var seen = existing.Select(e => (e.EntityId, e.Key, e.RunId)).ToHashSet();
        var fresh = tags.Where(t => !seen.Contains((t.EntityId, t.Key, t.RunId))).ToList();
        if (fresh.Count == 0) return;

        await _db.EntityTags.AddRangeAsync(fresh.Select(t => new EntityTagRow
        {
            Id = Guid.NewGuid(),
            ProjectId = t.ProjectId,
            EntityId = t.EntityId,
            EntityName = t.EntityName,
            Key = t.Key,
            RunId = t.RunId,
            JobId = t.JobId,
            CreatedAt = _clock.UtcNow,
        }), ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> RunsForEntityAsync(Guid entityId, int take = 20, CancellationToken ct = default)
        => await _db.EntityTags.AsNoTracking()
            .Where(t => t.EntityId == entityId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => t.RunId)
            .Distinct()
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<EntityTagPair>> PairsForEntityAsync(Guid entityId, int take = 60, CancellationToken ct = default)
        => await _db.EntityTags.AsNoTracking()
            .Where(t => t.EntityId == entityId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .Select(t => new EntityTagPair(t.Key, t.RunId, t.JobId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<EntityTagHit>> SearchKeysAsync(string term, int take = 10, CancellationToken ct = default)
        => await _db.EntityTags.AsNoTracking()
            .Where(t => EF.Functions.ILike(t.Key, $"%{term}%"))
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new EntityTagHit(t.ProjectId, t.EntityName, t.Key))
            .Distinct()
            .Take(Math.Clamp(take, 1, 50))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> RunsForKeyAsync(Guid entityId, string key, int take = 20, CancellationToken ct = default)
        => await _db.EntityTags.AsNoTracking()
            .Where(t => t.EntityId == entityId && t.Key == key)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => t.RunId)
            .Distinct()
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(ct);
}
