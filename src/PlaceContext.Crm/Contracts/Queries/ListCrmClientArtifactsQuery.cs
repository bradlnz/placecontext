using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;
public sealed record ListCrmClientArtifactsQuery(Guid ClientId, int Take = 200)
    : IQuery<IReadOnlyList<CrmClientArtifactView>>;
