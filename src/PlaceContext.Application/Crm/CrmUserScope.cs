using System.Linq;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using System;

namespace PlaceContext.Application.Features;

public sealed class CrmUserScope
{
    private readonly ICurrentUser _currentUser;
    private readonly ICrmUserRepository _crmUsers;
    private readonly ICrmClientUserAssignmentRepository _assignments;

    public CrmUserScope(
        ICurrentUser currentUser,
        ICrmUserRepository crmUsers,
        ICrmClientUserAssignmentRepository assignments)
        => (_currentUser, _crmUsers, _assignments) = (currentUser, crmUsers, assignments);

    public async Task EnsureClientAccessAsync(
        Guid projectId,
        Guid clientId,
        CancellationToken ct = default)
    {
        var allowedClientIds = await GetAllowedClientIdsAsync(projectId, ct);
        if (allowedClientIds is null)
            return;

        if (!allowedClientIds.Contains(clientId))
            throw new InvalidOperationException("The selected CRM contact is not assigned to this user.");
    }

    public async Task<IReadOnlyList<T>> FilterByAccessAsync<T>(
        Guid projectId,
        IReadOnlyList<T> values,
        Func<T, Guid> getClientId,
        CancellationToken ct = default)
    {
        var allowedClientIds = await GetAllowedClientIdsAsync(projectId, ct);
        return allowedClientIds is null
            ? values
            : values.Where(value => allowedClientIds.Contains(getClientId(value))).ToList();
    }

    private async Task<HashSet<Guid>?> GetAllowedClientIdsAsync(Guid projectId, CancellationToken ct)
    {
        var isCrmUserRole = string.Equals(
            _currentUser.Role,
            UserRole.CrmUser.ToString(),
            StringComparison.OrdinalIgnoreCase);

        var crmUser = await _crmUsers.GetByAuthUserIdAsync(_currentUser.UserId, ct);
        if (isCrmUserRole && crmUser is null)
            throw new InvalidOperationException("This CRM user is not linked to a CRM access profile.");
        if (isCrmUserRole)
            return new HashSet<Guid>();

        if (crmUser is null)
            return null;

        return (await _assignments.ListForCrmUserAsync(projectId, crmUser.Id, ct))
            .ToHashSet();
    }
}
