namespace PlaceContext.Application.Features;

public sealed class EntityRunsHandler : Cqrs.IQueryHandler<EntityRunsQuery, IReadOnlyList<Guid>>
{
    private readonly IEntityTagStore _tags;

    public EntityRunsHandler(IEntityTagStore tags) => _tags = tags;

    public Task<IReadOnlyList<Guid>> HandleAsync(EntityRunsQuery query, CancellationToken ct = default)
        => _tags.RunsForEntityAsync(query.EntityId);
}
