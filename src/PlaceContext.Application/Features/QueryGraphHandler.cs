using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class QueryGraphHandler : IQueryHandler<QueryGraphQuery, GraphQueryView>
{
    private readonly IDecisionTreeProvider _tree;

    public QueryGraphHandler(IDecisionTreeProvider tree) => _tree = tree;

    public async Task<GraphQueryView> HandleAsync(QueryGraphQuery query, CancellationToken ct = default)
    {
        var tree = await _tree.BuildAsync(ProjectId.From(query.ProjectId), ct);
        return new GraphQueryView(query.Question, tree.Answer(query.Question));
    }
}
