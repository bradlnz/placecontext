namespace PlaceContext.Application.Features;

/// <summary>Persistence port for entity tags (a link store, not an aggregate — like the tool-call log).</summary>
public interface IEntityTagStore
{
    /// <summary>Insert tags, silently skipping ones that already exist for (entity, key, run).</summary>
    Task AddAsync(IReadOnlyList<EntityTag> tags, CancellationToken ct = default);

    /// <summary>Run ids whose output was tagged with this entity key (newest first).</summary>
    Task<IReadOnlyList<Guid>> RunsForKeyAsync(Guid entityId, string key, int take = 20, CancellationToken ct = default);

    /// <summary>All run ids tagged against this entity, any key (newest first).</summary>
    Task<IReadOnlyList<Guid>> RunsForEntityAsync(Guid entityId, int take = 20, CancellationToken ct = default);

    /// <summary>The tag pairs for an entity — which key value each run was linked through.</summary>
    Task<IReadOnlyList<EntityTagPair>> PairsForEntityAsync(Guid entityId, int take = 60, CancellationToken ct = default);

    /// <summary>Search the tag index: key values containing the term, as graph nodes.</summary>
    Task<IReadOnlyList<EntityTagHit>> SearchKeysAsync(string term, int take = 10, CancellationToken ct = default);
}
