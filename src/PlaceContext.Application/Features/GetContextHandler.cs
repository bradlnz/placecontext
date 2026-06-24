using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetContextHandler : IQueryHandler<GetContextQuery, ProjectContextView>
{
    private readonly IProjectContextRepository _contexts;
    public GetContextHandler(IProjectContextRepository contexts) => _contexts = contexts;

    public async Task<ProjectContextView> HandleAsync(GetContextQuery query, CancellationToken ct = default)
    {
        var context = await _contexts.GetForProjectAsync(ProjectId.From(query.ProjectId), ct);
        return context is null ? ProjectContextView.Empty(query.ProjectId) : ViewMapper.ToView(context);
    }
}
