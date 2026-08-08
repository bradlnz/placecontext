using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;
public sealed record GetCrmCommsCapabilitiesQuery : IQuery<CrmCommsCapabilitiesView>;
