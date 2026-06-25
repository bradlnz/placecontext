using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class RecomputeRiskHandler : ICommandHandler<RecomputeRiskCommand, RiskDashboardView>
{
    private readonly IProjectRepository _projects;
    private readonly RiskAssessmentService _risk;
    private readonly IUnitOfWork _uow;

    public RecomputeRiskHandler(IProjectRepository projects, RiskAssessmentService risk, IUnitOfWork uow)
    {
        _projects = projects;
        _risk = risk;
        _uow = uow;
    }

    public async Task<RiskDashboardView> HandleAsync(RecomputeRiskCommand command, CancellationToken ct = default)
    {
        var project = await _projects.GetByIdAsync(ProjectId.From(command.ProjectId), ct)
            ?? throw new InvalidOperationException($"Project {command.ProjectId} not found.");

        var assessment = await _risk.AssessAsync(project, ct);
        await _projects.UpdateAsync(project, ct);
        await _uow.SaveChangesAsync(ct);

        return ViewMapper.ToDashboard(assessment);
    }
}
