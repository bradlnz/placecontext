using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class GetTriggerByIdHandler : IQueryHandler<GetTriggerByIdQuery, TriggerView?>
{
    private readonly IJobTriggerRepository _triggers;
    public GetTriggerByIdHandler(IJobTriggerRepository triggers) => _triggers = triggers;

    public async Task<TriggerView?> HandleAsync(GetTriggerByIdQuery query, CancellationToken ct = default)
    {
        var trigger = await _triggers.GetByIdAsync(query.TriggerId, ct);
        return trigger is null ? null : TriggerViewMapper.ToView(trigger);
    }
}
