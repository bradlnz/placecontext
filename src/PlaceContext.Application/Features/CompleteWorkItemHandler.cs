using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class CompleteWorkItemHandler : ICommandHandler<CompleteWorkItemCommand, WorkItemView>
{
    private readonly IWorkItemRepository _workItems;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public CompleteWorkItemHandler(IWorkItemRepository workItems, IUnitOfWork uow, IClock clock)
    {
        _workItems = workItems;
        _uow = uow;
        _clock = clock;
    }

    public async Task<WorkItemView> HandleAsync(CompleteWorkItemCommand command, CancellationToken ct = default)
    {
        var item = await _workItems.GetByIdAsync(command.WorkItemId, ct)
            ?? throw new InvalidOperationException($"Work item {command.WorkItemId} not found.");

        item.Complete(_clock.UtcNow);
        await _workItems.UpdateAsync(item, ct);
        await _uow.SaveChangesAsync(ct);
        return ViewMapper.ToView(item);
    }
}
