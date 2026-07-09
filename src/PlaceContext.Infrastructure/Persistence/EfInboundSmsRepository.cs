using Microsoft.EntityFrameworkCore;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfInboundSmsRepository : IInboundSmsRepository
{
    private readonly AppDbContext _db;
    public EfInboundSmsRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(SmsMessage message, CancellationToken ct = default)
        => await _db.SmsMessages.AddAsync(ToRow(message), ct);

    public async Task<IReadOnlyList<SmsMessage>> ListRecentAsync(int take = 50, CancellationToken ct = default)
    {
        var rows = await _db.SmsMessages.AsNoTracking()
            .OrderByDescending(r => r.ReceivedAt)
            .Take(take)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public Task<bool> ExistsAsync(string provider, string externalId, CancellationToken ct = default)
        => _db.SmsMessages.AsNoTracking().AnyAsync(r => r.Provider == provider && r.ExternalId == externalId, ct);

    private static SmsMessageRow ToRow(SmsMessage m) => new()
    {
        Id = m.Id,
        ProjectId = m.ProjectId,
        FromProtected = m.FromProtected,
        To = m.To,
        BodyProtected = m.BodyProtected,
        Provider = m.Provider,
        ExternalId = m.ExternalId,
        ReceivedAt = m.ReceivedAt,
    };

    private static SmsMessage ToDomain(SmsMessageRow r) => SmsMessage.Rehydrate(
        r.Id, r.ProjectId, r.FromProtected, r.To, r.BodyProtected, r.Provider, r.ExternalId, r.ReceivedAt);
}
