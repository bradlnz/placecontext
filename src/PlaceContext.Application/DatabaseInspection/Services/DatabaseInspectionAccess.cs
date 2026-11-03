using PlaceContext.Application.Access;
using PlaceContext.Application.Tenancy;

namespace PlaceContext.Application.DatabaseInspection;

public sealed class DatabaseInspectionAccess(
    ICurrentTenant tenant, ICurrentUser user, IWorkspaceAdministratorReader administrators)
{
    public Task<bool> CanReadAsync(CancellationToken ct = default)
    {
        if (!tenant.IsResolved || tenant.TenantId == Guid.Empty || !user.IsAuthenticated || user.UserId == Guid.Empty)
        {
            return Task.FromResult(false);
        }
        return administrators.IsOwnerOrAdministratorAsync(tenant.TenantId, user.UserId, ct);
    }

    public async Task EnsureAsync(CancellationToken ct = default)
    {
        if (!await CanReadAsync(ct))
        {
            throw new UnauthorizedAccessException("Only workspace owners and super admins can inspect the PlaceContext database.");
        }
    }
}
