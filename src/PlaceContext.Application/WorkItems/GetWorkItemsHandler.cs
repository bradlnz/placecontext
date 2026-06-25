using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetWorkItemsHandler : IQueryHandler<GetWorkItemsQuery, IReadOnlyList<WorkItemView>>
{
    private readonly IWorkItemRepository _workItems;
    public GetWorkItemsHandler(IWorkItemRepository workItems) => _workItems = workItems;

    public async Task<IReadOnlyList<WorkItemView>> HandleAsync(GetWorkItemsQuery query, CancellationToken ct = default)
    {
        var items = await _workItems.ListForProjectAsync(ProjectId.From(query.ProjectId), ct);
        return items.Select(ViewMapper.ToView).ToList();
    }
}
