using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;
public sealed record ListCrmAppointmentsQuery(Guid ProjectId)
    : IQuery<IReadOnlyList<CrmAppointmentView>>;
