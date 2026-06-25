using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListTriggersHandler : IQueryHandler<ListTriggersQuery, IReadOnlyList<TriggerView>>
{
    private readonly IJobTriggerRepository _triggers;

    public ListTriggersHandler(IJobTriggerRepository triggers) => _triggers = triggers;

    public async Task<IReadOnlyList<TriggerView>> HandleAsync(ListTriggersQuery query, CancellationToken ct = default)
    {
        var triggers = await _triggers.ListForProjectAsync(query.ProjectId, ct);
        return triggers.Select(TriggerViewMapper.ToView).ToList();
    }
}
