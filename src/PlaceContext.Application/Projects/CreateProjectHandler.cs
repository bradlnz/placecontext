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
    private readonly RiskAssessmentService _risk;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public CreateProjectHandler(IProjectRepository projects, RiskAssessmentService risk, IUnitOfWork uow, IClock clock)
    {
        _projects = projects;
        _risk = risk;
        _uow = uow;
        _clock = clock;
    }

    public async Task<ProjectSummaryView> HandleAsync(CreateProjectCommand command, CancellationToken ct = default)
    {
        var path = ProjectPath.From(command.Path);
        var existing = await _projects.GetByPathAsync(path, ct);
        if (existing is not null)
            return ViewMapper.ToSummary(existing);

        var name = string.IsNullOrWhiteSpace(command.Name)
            ? command.Path.TrimEnd('/').Split('/').LastOrDefault() ?? command.Path
            : command.Name;

        var project = Project.Discover(path, ProjectName.From(name), _clock.UtcNow);
        project.Register(_clock.UtcNow);
        await _projects.AddAsync(project, ct);
        await _uow.SaveChangesAsync(ct); // persist first so risk collaborators can load the project by id

        // Assess risk up front so a brand-new project lands on the dashboards with real scores
        // (technical risk from its code metrics; process risk from its — initially empty — ledger).
        await _risk.AssessAsync(project, ct);
        await _projects.UpdateAsync(project, ct);
        await _uow.SaveChangesAsync(ct);

        return ViewMapper.ToSummary(project);
    }
}
