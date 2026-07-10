using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class NextWorkItemHandler : ICommandHandler<NextWorkItemCommand, WorkItemView?>
{
    private readonly IWorkItemRepository _workItems;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public NextWorkItemHandler(IWorkItemRepository workItems, IUnitOfWork uow, IClock clock)
    {
        _workItems = workItems;
        _uow = uow;
        _clock = clock;
    }

    public async Task<WorkItemView?> HandleAsync(NextWorkItemCommand command, CancellationToken ct = default)
    {
        var item = await _workItems.NextQueuedAsync(ProjectId.From(command.ProjectId), ct);
        if (item is null) return null;

        item.Claim(_clock.UtcNow);
        await _workItems.UpdateAsync(item, ct);
        await _uow.SaveChangesAsync(ct);
        return ViewMapper.ToView(item);
    }
}
