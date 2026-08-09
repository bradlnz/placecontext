using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetProjectOverviewHandler : IQueryHandler<GetProjectOverviewQuery, ProjectOverviewView>
{
    private readonly IProjectRepository _projects;
    private readonly IActivityLogRepository _ledgers;

    public GetProjectOverviewHandler(IProjectRepository projects, IActivityLogRepository ledgers)
    {
        _projects = projects;
        _ledgers = ledgers;
    }

    public async Task<ProjectOverviewView> HandleAsync(GetProjectOverviewQuery query, CancellationToken ct = default)
    {
        var id = ProjectId.From(query.ProjectId);
        var p = await _projects.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException($"Project {query.ProjectId} not found.");
        var ledger = await _ledgers.GetForProjectAsync(id, ct);

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
            ledger.Count);
    }
}
