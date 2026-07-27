namespace PlaceContext.Application.Features;

public sealed class TaggedRunsHandler : Cqrs.IQueryHandler<TaggedRunsQuery, IReadOnlyList<Guid>>
{
    private readonly IEntityTagStore _tags;

    public TaggedRunsHandler(IEntityTagStore tags) => _tags = tags;

    public Task<IReadOnlyList<Guid>> HandleAsync(TaggedRunsQuery query, CancellationToken ct = default)
        => _tags.RunsForKeyAsync(query.EntityId, query.Key);
}

public sealed class EntityRunsHandler : Cqrs.IQueryHandler<EntityRunsQuery, IReadOnlyList<Guid>>
{
    private readonly IEntityTagStore _tags;

    public EntityRunsHandler(IEntityTagStore tags) => _tags = tags;

    public Task<IReadOnlyList<Guid>> HandleAsync(EntityRunsQuery query, CancellationToken ct = default)
        => _tags.RunsForEntityAsync(query.EntityId);
}

public sealed class EntityTagPairsHandler : Cqrs.IQueryHandler<EntityTagPairsQuery, IReadOnlyList<EntityTagPair>>
{
    private readonly IEntityTagStore _tags;

    public EntityTagPairsHandler(IEntityTagStore tags) => _tags = tags;

    public Task<IReadOnlyList<EntityTagPair>> HandleAsync(EntityTagPairsQuery query, CancellationToken ct = default)
        => _tags.PairsForEntityAsync(query.EntityId);
}
