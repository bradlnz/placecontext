using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetTimelineHandler : IQueryHandler<GetTimelineQuery, ActivityTimelineView>
{
    private readonly IActivityLogRepository _ledgers;
    public GetTimelineHandler(IActivityLogRepository ledgers) => _ledgers = ledgers;

    public async Task<ActivityTimelineView> HandleAsync(GetTimelineQuery query, CancellationToken ct = default)
    {
        var id = ProjectId.From(query.ProjectId);
        var ledger = await _ledgers.GetForProjectAsync(id, ct);
        var rows = ledger.Records
            .OrderByDescending(r => r.Sequence)
            .Take(query.Take)
            .Select(ViewMapper.ToView)
            .ToList();
        return new ActivityTimelineView(query.ProjectId, rows);
    }
}
