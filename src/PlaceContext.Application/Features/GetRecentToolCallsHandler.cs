using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetRecentToolCallsHandler : IQueryHandler<GetRecentToolCallsQuery, IReadOnlyList<ToolCallView>>
{
    private readonly IToolCallLog _log;
    public GetRecentToolCallsHandler(IToolCallLog log) => _log = log;

    public Task<IReadOnlyList<ToolCallView>> HandleAsync(GetRecentToolCallsQuery query, CancellationToken ct = default)
    {
        var views = _log.Recent(query.Take)
            .Select(e => new ToolCallView(
                e.Id, e.Tool, e.Direction, e.Project, e.Summary,
                e.Status.ToString(), e.DurationMs, e.RequestJson, e.ResponseJson, e.At))
            .ToList();
        return Task.FromResult<IReadOnlyList<ToolCallView>>(views);
    }
}
