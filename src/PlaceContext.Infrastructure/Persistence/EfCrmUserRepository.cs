using Microsoft.EntityFrameworkCore;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class EfCrmUserRepository : ICrmUserRepository
{
    private readonly AppDbContext _db;

    public EfCrmUserRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(CrmUser user, CancellationToken ct = default)
        => await _db.CrmUsers.AddAsync(ToRow(user), ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var user = await _db.CrmUsers.FindAsync(new object[] { id }, ct);
        if (user is null) return;
        var assignments = await _db.CrmClientUserAssignments
            .Where(assignment => assignment.CrmUserId == id)
            .ToListAsync(ct);
        _db.CrmClientUserAssignments.RemoveRange(assignments);
        _db.CrmUsers.Remove(user);
    }

    public async Task<CrmUser?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.CrmUsers.AsNoTracking().FirstOrDefaultAsync(user => user.Id == id, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<CrmUser?> GetByAuthUserIdAsync(Guid authUserId, CancellationToken ct = default)
    {
        var row = await _db.CrmUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.AuthUserId == authUserId, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<CrmUser?> GetByEmailAsync(Guid projectId, string email, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var row = await _db.CrmUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.ProjectId == projectId && user.Email.ToLower() == normalized, ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<CrmUser?> GetByJoinCodeAsync(
        string joinCode,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var row = await _db.CrmUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(user =>
                user.JoinCode == joinCode
                && user.OnboardedAt == null
                && user.JoinCodeExpiresAt != null
                && user.JoinCodeExpiresAt > now,
                ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<bool> MarkOnboardedByJoinCodeAsync(
        string joinCode,
        Guid authUserId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var row = await _db.CrmUsers
            .FirstOrDefaultAsync(user =>
                user.JoinCode == joinCode
                && user.OnboardedAt == null
                && user.JoinCodeExpiresAt != null
                && user.JoinCodeExpiresAt > now,
                ct);
        if (row is null)
            return false;

        row.AuthUserId = authUserId;
        row.OnboardedAt = now;
        row.JoinCode = null;
        row.JoinCodeExpiresAt = null;
        row.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<CrmUser>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
        => (await _db.CrmUsers
            .AsNoTracking()
            .Where(user => user.ProjectId == projectId)
            .ToListAsync(ct))
            .OrderBy(user => user.Name ?? user.Email)
            .ThenBy(user => user.Email)
            .Select(ToDomain)
            .ToList();

    private static CrmUserRow ToRow(CrmUser user) => new()
    {
        Id = user.Id,
        ProjectId = user.ProjectId,
        Name = user.Name,
        Email = user.Email,
        JoinCode = user.JoinCode,
        JoinCodeExpiresAt = user.JoinCodeExpiresAt,
        AuthUserId = user.AuthUserId,
        OnboardedAt = user.OnboardedAt,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt,
    };

    private static CrmUser ToDomain(CrmUserRow row) =>
        CrmUser.Rehydrate(
            row.Id,
            row.ProjectId,
            row.Name,
            row.Email,
            row.CreatedAt,
            row.UpdatedAt,
            row.JoinCode,
            row.JoinCodeExpiresAt,
            row.AuthUserId,
            row.OnboardedAt);
}
