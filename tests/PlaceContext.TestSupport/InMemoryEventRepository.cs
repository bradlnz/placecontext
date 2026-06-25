using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.TestSupport;

public sealed class InMemoryEventRepository : IEventRepository
{
    private readonly List<EventDefinition> _definitions = new();
    public List<EventOccurrence> Occurrences { get; } = new();

    public Task AddDefinitionAsync(EventDefinition definition, CancellationToken ct = default)
    {
        _definitions.Add(definition);
        return Task.CompletedTask;
    }

    public Task UpdateDefinitionAsync(EventDefinition definition, CancellationToken ct = default)
        => Task.CompletedTask; // reference mutated in place

    public Task<EventDefinition?> FindDefinitionByNameAsync(string name, CancellationToken ct = default)
        => Task.FromResult(_definitions.FirstOrDefault(
            d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<EventDefinition>> ListDefinitionsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<EventDefinition>>(_definitions.OrderBy(d => d.Name).ToList());

    public Task AddOccurrenceAsync(EventOccurrence occurrence, CancellationToken ct = default)
    {
        Occurrences.Add(occurrence);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<EventOccurrence>> ListOccurrencesAsync(int take, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<EventOccurrence>>(
            Occurrences.OrderByDescending(o => o.OccurredAt).Take(take).ToList());
}
