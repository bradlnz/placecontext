using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Access;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Auth;

public sealed class EfWorkspaceAdministratorReader(AppDbContext db) : IWorkspaceAdministratorReader
{
    public Task<bool> IsOwnerOrAdministratorAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
        => db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == userId && u.TenantId == tenantId)
            .Select(u => u.IsDefaultAdmin || u.IsExternalAdmin || u.Role == nameof(UserRole.Owner))
            .FirstOrDefaultAsync(ct);

    public Task<bool> IsAdministratorAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
        => db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == userId && u.TenantId == tenantId)
            .Select(u => u.IsDefaultAdmin || u.IsExternalAdmin).FirstOrDefaultAsync(ct);
}
