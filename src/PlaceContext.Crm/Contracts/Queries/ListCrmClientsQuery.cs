using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

public sealed record ListCrmClientsQuery(Guid ProjectId) : IQuery<IReadOnlyList<CrmClientView>>;
