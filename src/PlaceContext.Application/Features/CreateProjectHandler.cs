using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class CreateProjectHandler : ICommandHandler<CreateProjectCommand, ProjectSummaryView>
{
    private readonly IProjectRepository _projects;
    private readonly DebtAssessmentService _debt;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public CreateProjectHandler(IProjectRepository projects, DebtAssessmentService debt, IUnitOfWork uow, IClock clock)
    {
        _projects = projects;
        _debt = debt;
        _uow = uow;
        _clock = clock;
    }

    public async Task<ProjectSummaryView> HandleAsync(CreateProjectCommand command, CancellationToken ct = default)
    {
        var path = RepoPath.From(command.Path);
        var existing = await _projects.GetByPathAsync(path, ct);
        if (existing is not null)
            return ViewMapper.ToSummary(existing);

        var name = string.IsNullOrWhiteSpace(command.Name)
            ? command.Path.TrimEnd('/').Split('/').LastOrDefault() ?? command.Path
            : command.Name;

        var project = Project.Discover(path, ProjectName.From(name), _clock.UtcNow);
        project.Register(_clock.UtcNow);
        await _projects.AddAsync(project, ct);
        await _uow.SaveChangesAsync(ct); // persist first so debt collaborators can load the project by id

        // Assess debt up front so a brand-new project lands on the dashboards with real scores
        // (technical debt from its code metrics; agentic debt from its — initially empty — ledger).
        await _debt.AssessAsync(project, ct);
        await _projects.UpdateAsync(project, ct);
        await _uow.SaveChangesAsync(ct);

        return ViewMapper.ToSummary(project);
    }
}
