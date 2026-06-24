using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetTimelineHandler : IQueryHandler<GetTimelineQuery, ChangeTimelineView>
{
    private readonly IChangeLedgerRepository _ledgers;
    public GetTimelineHandler(IChangeLedgerRepository ledgers) => _ledgers = ledgers;

    public async Task<ChangeTimelineView> HandleAsync(GetTimelineQuery query, CancellationToken ct = default)
    {
        var id = ProjectId.From(query.ProjectId);
        var ledger = await _ledgers.GetForProjectAsync(id, ct);
        var rows = ledger.Records
            .OrderByDescending(r => r.Sequence)
            .Take(query.Take)
            .Select(ViewMapper.ToView)
            .ToList();
        return new ChangeTimelineView(query.ProjectId, rows);
    }
}
