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

    public Task<bool> HasAnyMembersAsync(CancellationToken ct = default)
        => _db.Users.AsNoTracking().AnyAsync(ct);

    public async Task<AuthUser?> RegisterAsync(string email, string displayName, string password, UserRole role, CancellationToken ct = default)
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
            Role = role.ToString(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await _db.Users.AddAsync(row, ct);
        await _db.SaveChangesAsync(ct); // TenantId stamped here
        return ToAuthUser(row);
    }

    public async Task<AuthUser> GetOrCreateOperatorAsync(CancellationToken ct = default)
    {
        // The first user in the tenant is the operator. AsNoTracking + oldest-first keeps this stable
        // even if invites later add more members; the operator is whoever the deployment was created for.
        var existing = await _db.Users.AsNoTracking().OrderBy(u => u.CreatedAt).FirstOrDefaultAsync(ct);
        if (existing is not null)
            return ToAuthUser(existing);

        var row = new UserRow
        {
            Id = Guid.NewGuid(),
            Email = "operator@localhost",
            DisplayName = "Operator",
            // Password login is removed; this hash is unusable (a random secret nobody holds), present
            // only to satisfy the non-null column. Sign-in happens via the portal token, never a password.
            PasswordHash = PasswordHasher.Hash(Guid.NewGuid().ToString("N")),
            Role = UserRole.Owner.ToString(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await _db.Users.AddAsync(row, ct);
        await _db.SaveChangesAsync(ct); // TenantId stamped here
        return ToAuthUser(row);
    }

    private static AuthUser ToAuthUser(UserRow r) => new(
        r.Id, r.TenantId, r.Email, r.DisplayName,
        Enum.TryParse<UserRole>(r.Role, out var role) ? role : UserRole.Member);

    private static string Normalize(string email) => (email ?? string.Empty).Trim().ToLowerInvariant();
}
