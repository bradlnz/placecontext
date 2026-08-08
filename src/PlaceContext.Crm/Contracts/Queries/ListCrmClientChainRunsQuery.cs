using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;
public sealed record ListCrmClientChainRunsQuery(Guid ClientId, int Take = 20)
    : IQuery<IReadOnlyList<CrmChainRunView>>;
