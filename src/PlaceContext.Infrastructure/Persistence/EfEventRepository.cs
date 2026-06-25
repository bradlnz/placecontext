using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfEventRepository : IEventRepository
{
    private readonly AppDbContext _db;

    public EfEventRepository(AppDbContext db) => _db = db;

    // ── Definitions ──────────────────────────────────────────────────────────────────────────────

    public async Task AddDefinitionAsync(EventDefinition definition, CancellationToken ct = default)
        => await _db.EventDefinitions.AddAsync(ToRow(definition), ct);

    public async Task UpdateDefinitionAsync(EventDefinition definition, CancellationToken ct = default)
    {
        var existing = await _db.EventDefinitions.FindAsync(new object[] { definition.Id }, ct);
        if (existing is null) return;
        existing.Description = definition.Description;
        existing.PayloadSchema = definition.PayloadSchema;
        existing.UpdatedAt = definition.UpdatedAt;
    }

    public async Task<EventDefinition?> FindDefinitionByNameAsync(string name, CancellationToken ct = default)
    {
        var row = await _db.EventDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Name.ToLower() == name.ToLower(), ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<EventDefinition>> ListDefinitionsAsync(CancellationToken ct = default)
    {
        var rows = await _db.EventDefinitions.AsNoTracking()
            .OrderBy(d => d.Name)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    // ── Occurrences ──────────────────────────────────────────────────────────────────────────────

    public async Task AddOccurrenceAsync(EventOccurrence occurrence, CancellationToken ct = default)
        => await _db.EventOccurrences.AddAsync(ToRow(occurrence), ct);

    public async Task<IReadOnlyList<EventOccurrence>> ListOccurrencesAsync(int take, CancellationToken ct = default)
    {
        var rows = await _db.EventOccurrences.AsNoTracking()
            .OrderByDescending(o => o.OccurredAt)
            .Take(take)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    // ── Mapping ──────────────────────────────────────────────────────────────────────────────────

    private static EventDefinitionRow ToRow(EventDefinition d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Description = d.Description,
        PayloadSchema = d.PayloadSchema,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
    };

    private static EventDefinition ToDomain(EventDefinitionRow r) =>
        EventDefinition.Rehydrate(r.Id, r.Name, r.Description, r.PayloadSchema, r.CreatedAt, r.UpdatedAt);

    private static EventOccurrenceRow ToRow(EventOccurrence o) => new()
    {
        Id = o.Id,
        Name = o.Name,
        Source = o.Source.ToString(),
        ProjectId = o.ProjectId,
        Payload = o.Payload,
        OccurredAt = o.OccurredAt,
    };

    private static EventOccurrence ToDomain(EventOccurrenceRow r) => EventOccurrence.Rehydrate(
        r.Id, r.Name, Enum.Parse<EventSource>(r.Source), r.ProjectId, r.Payload, r.OccurredAt);
}
