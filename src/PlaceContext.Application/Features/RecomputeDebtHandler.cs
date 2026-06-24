using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class RecomputeDebtHandler : ICommandHandler<RecomputeDebtCommand, DebtDashboardView>
{
    private readonly IProjectRepository _projects;
    private readonly DebtAssessmentService _debt;
    private readonly IUnitOfWork _uow;

    public RecomputeDebtHandler(IProjectRepository projects, DebtAssessmentService debt, IUnitOfWork uow)
    {
        _projects = projects;
        _debt = debt;
        _uow = uow;
    }

    public async Task<DebtDashboardView> HandleAsync(RecomputeDebtCommand command, CancellationToken ct = default)
    {
        var project = await _projects.GetByIdAsync(ProjectId.From(command.ProjectId), ct)
            ?? throw new InvalidOperationException($"Project {command.ProjectId} not found.");

        var assessment = await _debt.AssessAsync(project, ct);
        await _projects.UpdateAsync(project, ct);
        await _uow.SaveChangesAsync(ct);

        return ViewMapper.ToDashboard(assessment);
    }
}
