namespace PlaceContext.Application.Features;

public sealed class TaggedRunsHandler : Cqrs.IQueryHandler<TaggedRunsQuery, IReadOnlyList<Guid>>
{
    private readonly IEntityTagStore _tags;

    public TaggedRunsHandler(IEntityTagStore tags) => _tags = tags;

    public Task<IReadOnlyList<Guid>> HandleAsync(TaggedRunsQuery query, CancellationToken ct = default)
        => _tags.RunsForKeyAsync(query.EntityId, query.Key);
}
