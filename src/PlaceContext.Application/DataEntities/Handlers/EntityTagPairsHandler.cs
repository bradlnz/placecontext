namespace PlaceContext.Application.Features;

public sealed class EntityTagPairsHandler : Cqrs.IQueryHandler<EntityTagPairsQuery, IReadOnlyList<EntityTagPair>>
{
    private readonly IEntityTagStore _tags;

    public EntityTagPairsHandler(IEntityTagStore tags) => _tags = tags;

    public Task<IReadOnlyList<EntityTagPair>> HandleAsync(EntityTagPairsQuery query, CancellationToken ct = default)
        => _tags.PairsForEntityAsync(query.EntityId);
}
