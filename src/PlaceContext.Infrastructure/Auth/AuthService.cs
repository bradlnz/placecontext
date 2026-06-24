using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Auth;

/// <summary>
/// Registration + credential validation against the <c>users</c> table. All queries are tenant-scoped
/// by the DbContext's global query filter, so a user can only ever be created in / matched within the
/// current tenant. The new user's <c>TenantId</c> is stamped automatically on save.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly ICurrentTenant _tenant;

    public AuthService(AppDbContext db, ICurrentTenant tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<bool> EmailExistsAsync(string email, CancellationToken ct = default)
        => await _db.Users.AsNoTracking().AnyAsync(u => u.Email == Normalize(email), ct);

    public async Task<AuthUser?> RegisterAsync(string email, string displayName, string password, CancellationToken ct = default)
    {
        email = Normalize(email);
        if (await _db.Users.AsNoTracking().AnyAsync(u => u.Email == email, ct))
            return null;

        var row = new UserRow
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? email.Split('@')[0] : displayName.Trim(),
            PasswordHash = PasswordHasher.Hash(password),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await _db.Users.AddAsync(row, ct);
        await _db.SaveChangesAsync(ct); // TenantId stamped here
        return new AuthUser(row.Id, row.TenantId, row.Email, row.DisplayName);
    }

    public async Task<AuthUser?> ValidateAsync(string email, string password, CancellationToken ct = default)
    {
        email = Normalize(email);
        var row = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email, ct);
        if (row is null || !PasswordHasher.Verify(password, row.PasswordHash))
            return null;
        return new AuthUser(row.Id, row.TenantId, row.Email, row.DisplayName);
    }

    private static string Normalize(string email) => (email ?? string.Empty).Trim().ToLowerInvariant();
}
