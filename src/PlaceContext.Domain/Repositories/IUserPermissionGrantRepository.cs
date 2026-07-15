namespace PlaceContext.Domain.Repositories;

/// <summary>
/// Tenant-scoped, per-user permission overrides layered on top of a member's role defaults. An allow
/// override grants a permission the role wouldn't otherwise have; a revoke override removes one it
/// would. No row for a given (user, permission) means "inherit the role default".
/// </summary>
public interface IUserPermissionGrantRepository
{
    /// <summary>All of one user's overrides, as permission → allowed.</summary>
    Task<IReadOnlyDictionary<string, bool>> ListForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Sets (inserts or updates) an explicit allow/revoke for one permission.</summary>
    Task UpsertAsync(Guid userId, string permission, bool allowed, CancellationToken ct = default);

    /// <summary>Removes the override so the permission reverts to inheriting the role default.</summary>
    Task RemoveAsync(Guid userId, string permission, CancellationToken ct = default);
}
