using Microsoft.EntityFrameworkCore;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfUserPermissionGrantRepository : IUserPermissionGrantRepository
{
    private readonly AppDbContext _db;
    public EfUserPermissionGrantRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyDictionary<string, bool>> ListForUserAsync(Guid userId, CancellationToken ct = default)
        => await _db.UserPermissionGrants.AsNoTracking().Where(g => g.UserId == userId)
            .ToDictionaryAsync(g => g.Permission, g => g.Allowed, ct);

    public async Task UpsertAsync(Guid userId, string permission, bool allowed, CancellationToken ct = default)
    {
        var row = await _db.UserPermissionGrants.FirstOrDefaultAsync(
            g => g.UserId == userId && g.Permission == permission, ct);
        if (row is null)
            _db.UserPermissionGrants.Add(new UserPermissionGrantRow { Id = Guid.NewGuid(), UserId = userId, Permission = permission, Allowed = allowed });
        else
            row.Allowed = allowed;
    }

    public async Task RemoveAsync(Guid userId, string permission, CancellationToken ct = default)
    {
        var row = await _db.UserPermissionGrants.FirstOrDefaultAsync(
            g => g.UserId == userId && g.Permission == permission, ct);
        if (row is not null) _db.UserPermissionGrants.Remove(row);
    }
}
