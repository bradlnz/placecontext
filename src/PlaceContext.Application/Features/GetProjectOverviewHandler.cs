using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetProjectOverviewHandler : IQueryHandler<GetProjectOverviewQuery, ProjectOverviewView>
{
    private readonly IProjectRepository _projects;
    private readonly IChangeLedgerRepository _ledgers;
    private readonly IDebtAssessmentRepository _assessments;
    private readonly IProjectContextRepository _contexts;

    public GetProjectOverviewHandler(
        IProjectRepository projects, IChangeLedgerRepository ledgers,
        IDebtAssessmentRepository assessments, IProjectContextRepository contexts)
    {
        _projects = projects;
        _ledgers = ledgers;
        _assessments = assessments;
        _contexts = contexts;
    }

    public async Task<ProjectOverviewView> HandleAsync(GetProjectOverviewQuery query, CancellationToken ct = default)
    {
        var id = ProjectId.From(query.ProjectId);
        var p = await _projects.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException($"Project {query.ProjectId} not found.");
        var ledger = await _ledgers.GetForProjectAsync(id, ct);
        var latest = await _assessments.GetLatestAsync(id, ct);
        var context = await _contexts.GetForProjectAsync(id, ct);

        return new ProjectOverviewView(
            p.Id.Value,
            p.Name.Value,
            p.Path.Value,
            p.Status.ToString(),
            p.RegisteredAt,
            p.LastGraph?.BuiltAt,
            p.LastGraph?.NodeCount ?? 0,
            p.LastGraph?.LinkCount ?? 0,
            (p.LastGraph?.GodNodes ?? Array.Empty<GodNode>()).Select(ViewMapper.ToView).ToList(),
            ViewMapper.ToDashboard(latest),
            ledger.Count,
            context?.Markdown ?? string.Empty);
    }
}
