using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListCrmAppointmentsHandler
    : IQueryHandler<ListCrmAppointmentsQuery, IReadOnlyList<CrmAppointmentView>>
{
    private readonly ICrmAppointmentRepository _appointments;
    private readonly ICrmClientRepository _clients;
    public ListCrmAppointmentsHandler(ICrmAppointmentRepository appointments, ICrmClientRepository clients)
        => (_appointments, _clients) = (appointments, clients);

    public async Task<IReadOnlyList<CrmAppointmentView>> HandleAsync(ListCrmAppointmentsQuery query, CancellationToken ct = default)
    {
        var clients = (await _clients.ListForProjectAsync(query.ProjectId, ct)).ToDictionary(x => x.Id, x => x.Name);
        return (await _appointments.ListForProjectAsync(query.ProjectId, ct))
            .Select(value => CreateCrmAppointmentHandler.Map(value,
                value.ClientId is { } id && clients.TryGetValue(id, out var name) ? name : null))
            .ToArray();
    }
}
