using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;
public sealed record ListCrmClientCommunicationsQuery(Guid ClientId, int Take = 100)
    : IQuery<IReadOnlyList<CrmCommunicationView>>;
