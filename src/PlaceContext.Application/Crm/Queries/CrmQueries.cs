using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

public sealed record ListCrmClientsQuery(Guid ProjectId) : IQuery<IReadOnlyList<CrmClientView>>;
public sealed record ListCrmClientChainRunsQuery(Guid ClientId, int Take = 20)
    : IQuery<IReadOnlyList<CrmChainRunView>>;
public sealed record ListCrmClientAssignedJobChainsQuery(
    Guid ClientId,
    Guid ProjectId) : IQuery<IReadOnlyList<Guid>>;
public sealed record ListCrmClientCommunicationsQuery(Guid ClientId, int Take = 100)
    : IQuery<IReadOnlyList<CrmCommunicationView>>;
public sealed record GetCrmCommsCapabilitiesQuery : IQuery<CrmCommsCapabilitiesView>;
public sealed record ListCrmClientArtifactsQuery(Guid ClientId, int Take = 200)
    : IQuery<IReadOnlyList<CrmClientArtifactView>>;
public sealed record ListCrmAppointmentsQuery(Guid ProjectId)
    : IQuery<IReadOnlyList<CrmAppointmentView>>;
public sealed record ListCrmCalendarsQuery(Guid ProjectId) : IQuery<IReadOnlyList<CrmCalendarView>>;
