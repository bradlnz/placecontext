using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

/// <summary>Persistence of user-defined <see cref="EventDefinition"/> types and the
/// <see cref="EventOccurrence"/> log (workspace-scoped, tenant-filtered).</summary>
public interface IEventRepository
{
    // ── Definitions ──────────────────────────────────────────────────────────────────────────────
    Task AddDefinitionAsync(EventDefinition definition, CancellationToken ct = default);
    Task UpdateDefinitionAsync(EventDefinition definition, CancellationToken ct = default);
    Task<EventDefinition?> FindDefinitionByNameAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<EventDefinition>> ListDefinitionsAsync(CancellationToken ct = default);

    // ── Occurrences ──────────────────────────────────────────────────────────────────────────────
    Task AddOccurrenceAsync(EventOccurrence occurrence, CancellationToken ct = default);
    Task<IReadOnlyList<EventOccurrence>> ListOccurrencesAsync(int take, CancellationToken ct = default);
}
