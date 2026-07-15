using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetProjectByIdHandler : IQueryHandler<GetProjectByIdQuery, ProjectSummaryView?>
{
    private readonly IProjectRepository _projects;
    public GetProjectByIdHandler(IProjectRepository projects) => _projects = projects;

    public async Task<ProjectSummaryView?> HandleAsync(GetProjectByIdQuery query, CancellationToken ct = default)
    {
        // ProjectId.From rejects Guid.Empty — treat that the same as "not found" rather than a 500.
        if (query.ProjectId == Guid.Empty) return null;
        var project = await _projects.GetByIdAsync(ProjectId.From(query.ProjectId), ct);
        return project is null ? null : ViewMapper.ToSummary(project);
    }
}
