using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Crm.Infrastructure.Persistence;

public sealed class EfCrmAppointmentRepository : ICrmAppointmentRepository
{
    private readonly CrmDbContext _db;
    private readonly IDataEncryptor _encryptor;
    public EfCrmAppointmentRepository(CrmDbContext db, IDataEncryptor encryptor)
        => (_db, _encryptor) = (db, encryptor);

    public async Task AddAsync(CrmAppointment appointment, CancellationToken ct = default)
        => await _db.CrmAppointments.AddAsync(new CrmAppointmentRow
        {
            Id = appointment.Id, ProjectId = appointment.ProjectId, CalendarId = appointment.CalendarId, ClientId = appointment.ClientId,
            TitleProtected = Protect(appointment.Title)!, StartsAt = appointment.StartsAt, EndsAt = appointment.EndsAt,
            LocationProtected = Protect(appointment.Location), NotesProtected = Protect(appointment.Notes),
            CreatedByUserId = appointment.CreatedByUserId, CreatedAt = appointment.CreatedAt,
        }, ct);

    public async Task<CrmAppointment?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.CrmAppointments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task UpdateAsync(CrmAppointment appointment, CancellationToken ct = default)
    {
        var row = await _db.CrmAppointments.SingleOrDefaultAsync(x => x.Id == appointment.Id, ct);
        if (row is null) return;
        row.CalendarId = appointment.CalendarId; row.ClientId = appointment.ClientId;
        row.TitleProtected = Protect(appointment.Title)!; row.StartsAt = appointment.StartsAt; row.EndsAt = appointment.EndsAt;
        row.LocationProtected = Protect(appointment.Location); row.NotesProtected = Protect(appointment.Notes);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.CrmAppointments.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is not null) _db.CrmAppointments.Remove(row);
    }

    public async Task<IReadOnlyList<CrmAppointment>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
        => (await _db.CrmAppointments.AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.StartsAt).ToListAsync(ct)).Select(ToDomain).ToArray();

    private CrmAppointment ToDomain(CrmAppointmentRow row) => CrmAppointment.Rehydrate(
        row.Id, row.ProjectId, row.CalendarId, row.ClientId, Unprotect(row.TitleProtected)!, row.StartsAt, row.EndsAt,
        Unprotect(row.LocationProtected), Unprotect(row.NotesProtected), row.CreatedByUserId, row.CreatedAt);
    private string? Protect(string? value) => value is null ? null : _encryptor.Protect(value, DataEncryptionPurpose.CrmAppointment);
    private string? Unprotect(string? value) => value is null ? null : _encryptor.Unprotect(value, DataEncryptionPurpose.CrmAppointment);
}
