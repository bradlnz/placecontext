using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class RegisterProjectHandler : ICommandHandler<RegisterProjectCommand, ProjectSummaryView>
{
    private readonly IProjectRepository _projects;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public RegisterProjectHandler(IProjectRepository projects, IUnitOfWork uow, IClock clock)
    {
        _projects = projects;
        _uow = uow;
        _clock = clock;
    }

    public async Task<ProjectSummaryView> HandleAsync(RegisterProjectCommand command, CancellationToken ct = default)
    {
        var project = await _projects.GetByIdAsync(ProjectId.From(command.ProjectId), ct)
            ?? throw new InvalidOperationException($"Project {command.ProjectId} not found.");

        project.Register(_clock.UtcNow);
        await _projects.UpdateAsync(project, ct);
        await _uow.SaveChangesAsync(ct);
        return ViewMapper.ToSummary(project);
    }
}
