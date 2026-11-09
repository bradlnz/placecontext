using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Assemble the requirements a project must follow: the global document, then the project's own.</summary>
public sealed record GetEffectiveRequirementsQuery(Guid ProjectId) : IQuery<EffectiveRequirementsView>;
