using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class MoveWorkItemHandler : ICommandHandler<MoveWorkItemCommand, WorkItemView>
{
    private readonly IWorkItemRepository _workItems;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public MoveWorkItemHandler(IWorkItemRepository workItems, IUnitOfWork uow, IClock clock)
    {
        _workItems = workItems;
        _uow = uow;
        _clock = clock;
    }

    public async Task<WorkItemView> HandleAsync(MoveWorkItemCommand command, CancellationToken ct = default)
    {
        var item = await _workItems.GetByIdAsync(command.WorkItemId, ct)
            ?? throw new InvalidOperationException($"Work item {command.WorkItemId} not found.");

        if (!Enum.TryParse<WorkItemStatus>(command.Status, ignoreCase: true, out var status))
            throw new ArgumentException($"Unknown work-item status '{command.Status}'.");

        item.MoveTo(status, _clock.UtcNow);
        await _workItems.UpdateAsync(item, ct);
        await _uow.SaveChangesAsync(ct);
        return ViewMapper.ToView(item);
    }
}
