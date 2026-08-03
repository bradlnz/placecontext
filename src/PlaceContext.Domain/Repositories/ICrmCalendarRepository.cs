using PlaceContext.Domain.Entities;
namespace PlaceContext.Domain.Repositories;
public interface ICrmCalendarRepository
{
    Task AddAsync(CrmCalendar calendar, CancellationToken ct = default);
    Task<CrmCalendar?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task UpdateAsync(CrmCalendar calendar, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CrmCalendar>> ListForProjectAsync(Guid projectId, CancellationToken ct = default);
}
