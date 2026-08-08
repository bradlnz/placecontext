using Microsoft.EntityFrameworkCore;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
namespace PlaceContext.Crm.Infrastructure.Persistence;
public sealed class EfCrmCalendarRepository : ICrmCalendarRepository
{
    private readonly CrmDbContext _db; public EfCrmCalendarRepository(CrmDbContext db) => _db = db;
    public async Task AddAsync(CrmCalendar calendar, CancellationToken ct = default) => await _db.CrmCalendars.AddAsync(ToRow(calendar), ct);
    public async Task<CrmCalendar?> GetByIdAsync(Guid id, CancellationToken ct = default) { var row = await _db.CrmCalendars.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); return row is null ? null : ToDomain(row); }
    public async Task UpdateAsync(CrmCalendar calendar, CancellationToken ct = default) { var row = await _db.CrmCalendars.SingleOrDefaultAsync(x => x.Id == calendar.Id, ct); if (row is null) return; row.Name = calendar.Name; row.Color = calendar.Color; row.UpdatedAt = calendar.UpdatedAt; }
    public async Task DeleteAsync(Guid id, CancellationToken ct = default) { var row = await _db.CrmCalendars.SingleOrDefaultAsync(x => x.Id == id, ct); if (row is not null) _db.CrmCalendars.Remove(row); }
    public async Task<IReadOnlyList<CrmCalendar>> ListForProjectAsync(Guid projectId, CancellationToken ct = default) => (await _db.CrmCalendars.AsNoTracking().Where(x => x.ProjectId == projectId).OrderBy(x => x.Name).ToListAsync(ct)).Select(ToDomain).ToArray();
    private static CrmCalendarRow ToRow(CrmCalendar x) => new() { Id=x.Id, ProjectId=x.ProjectId, Name=x.Name, Color=x.Color, CreatedAt=x.CreatedAt, UpdatedAt=x.UpdatedAt };
    private static CrmCalendar ToDomain(CrmCalendarRow x) => CrmCalendar.Rehydrate(x.Id,x.ProjectId,x.Name,x.Color,x.CreatedAt,x.UpdatedAt);
}
