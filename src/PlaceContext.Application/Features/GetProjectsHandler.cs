using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetProjectsHandler : IQueryHandler<GetProjectsQuery, IReadOnlyList<ProjectSummaryView>>
{
    private readonly IProjectRepository _projects;
    public GetProjectsHandler(IProjectRepository projects) => _projects = projects;

    public async Task<IReadOnlyList<ProjectSummaryView>> HandleAsync(GetProjectsQuery query, CancellationToken ct = default)
    {
        var all = await _projects.ListAsync(ct);
        return all.Select(ViewMapper.ToSummary).ToList();
    }
}
