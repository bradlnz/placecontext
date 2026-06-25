using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetProjectRequirementsHandler : IQueryHandler<GetProjectRequirementsQuery, RequirementsView>
{
    private readonly IRequirementsRepository _requirements;
    public GetProjectRequirementsHandler(IRequirementsRepository requirements) => _requirements = requirements;

    public async Task<RequirementsView> HandleAsync(GetProjectRequirementsQuery query, CancellationToken ct = default)
    {
        var doc = await _requirements.GetForProjectAsync(ProjectId.From(query.ProjectId), ct);
        return doc is null ? RequirementsView.EmptyForProject(query.ProjectId) : ViewMapper.ToView(doc);
    }
}
