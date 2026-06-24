using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class AddContextHandler : ICommandHandler<AddContextCommand, ProjectContextView>
{
    private readonly IProjectContextRepository _contexts;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public AddContextHandler(IProjectContextRepository contexts, IUnitOfWork uow, IClock clock)
    {
        _contexts = contexts;
        _uow = uow;
        _clock = clock;
    }

    public async Task<ProjectContextView> HandleAsync(AddContextCommand command, CancellationToken ct = default)
    {
        var projectId = ProjectId.From(command.ProjectId);
        var context = await _contexts.GetForProjectAsync(projectId, ct)
            ?? ProjectContext.Start(projectId, _clock.UtcNow);

        context.Append(command.Section, _clock.UtcNow);
        await _contexts.SaveAsync(context, ct);
        await _uow.SaveChangesAsync(ct);
        return ViewMapper.ToView(context);
    }
}
