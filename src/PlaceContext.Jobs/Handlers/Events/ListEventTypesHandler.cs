using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class ListEventTypesHandler : IQueryHandler<ListEventTypesQuery, IReadOnlyList<EventTypeView>>
{
    private readonly IEventRepository _events;

    public ListEventTypesHandler(IEventRepository events) => _events = events;

    public async Task<IReadOnlyList<EventTypeView>> HandleAsync(ListEventTypesQuery query, CancellationToken ct = default)
    {
        var builtIns = BuiltInEvents.All.Select(d =>
            new EventTypeView(d.Name, d.Description, IsBuiltIn: true, PayloadSchema: null, CreatedAt: null));

        var defined = (await _events.ListDefinitionsAsync(ct)).Select(d =>
            new EventTypeView(d.Name, d.Description, IsBuiltIn: false, d.PayloadSchema, d.CreatedAt));

        return builtIns.Concat(defined).ToList();
    }
}
