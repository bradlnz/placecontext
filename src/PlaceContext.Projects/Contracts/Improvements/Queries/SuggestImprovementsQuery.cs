using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

public sealed record SuggestImprovementsQuery(Guid ProjectId) : IQuery<ImprovementsView>;
