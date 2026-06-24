using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class AddWorkItemHandler : ICommandHandler<AddWorkItemCommand, WorkItemView>
{
    private readonly IProjectRepository _projects;
    private readonly IWorkItemRepository _workItems;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public AddWorkItemHandler(IProjectRepository projects, IWorkItemRepository workItems, IUnitOfWork uow, IClock clock)
    {
        _projects = projects;
        _workItems = workItems;
        _uow = uow;
        _clock = clock;
    }

    public async Task<WorkItemView> HandleAsync(AddWorkItemCommand command, CancellationToken ct = default)
    {
        var projectId = ProjectId.From(command.ProjectId);
        _ = await _projects.GetByIdAsync(projectId, ct)
            ?? throw new InvalidOperationException($"Project {command.ProjectId} not found.");

        var priority = Enum.TryParse<WorkItemPriority>(command.Priority, ignoreCase: true, out var p) ? p : WorkItemPriority.Normal;
        var item = WorkItem.Queue(projectId, command.Title, command.Detail, priority, _clock.UtcNow);
        await _workItems.AddAsync(item, ct);
        await _uow.SaveChangesAsync(ct);
        return ViewMapper.ToView(item);
    }
}
