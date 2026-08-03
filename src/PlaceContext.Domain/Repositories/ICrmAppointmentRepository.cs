using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

public interface ICrmAppointmentRepository
{
    Task AddAsync(CrmAppointment appointment, CancellationToken ct = default);
    Task<CrmAppointment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task UpdateAsync(CrmAppointment appointment, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CrmAppointment>> ListForProjectAsync(Guid projectId, CancellationToken ct = default);
}
