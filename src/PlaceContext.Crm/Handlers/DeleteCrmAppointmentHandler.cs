using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class DeleteCrmAppointmentHandler : ICommandHandler<DeleteCrmAppointmentCommand, bool>
{
    private readonly ICrmAppointmentRepository _appointments; private readonly IUnitOfWork _uow;
    public DeleteCrmAppointmentHandler(ICrmAppointmentRepository appointments, IUnitOfWork uow) => (_appointments, _uow) = (appointments, uow);
    public async Task<bool> HandleAsync(DeleteCrmAppointmentCommand command, CancellationToken ct = default)
    {
        if (await _appointments.GetByIdAsync(command.AppointmentId, ct) is null) return false;
        await _appointments.DeleteAsync(command.AppointmentId, ct); await _uow.SaveChangesAsync(ct); return true;
    }
}
