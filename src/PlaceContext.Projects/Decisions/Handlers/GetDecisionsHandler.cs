using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetDecisionsHandler
    : IQueryHandler<GetDecisionsQuery, IReadOnlyList<DecisionView>>
{
    private readonly IDecisionRepository _decisions;

    public GetDecisionsHandler(IDecisionRepository decisions) => _decisions = decisions;

    public async Task<IReadOnlyList<DecisionView>> HandleAsync(
        GetDecisionsQuery query,
        CancellationToken ct = default)
    {
        var decisions = await _decisions.ListForProjectAsync(ProjectId.From(query.ProjectId), ct);
        return decisions.Select(ViewMapper.ToView).ToList();
    }
}
