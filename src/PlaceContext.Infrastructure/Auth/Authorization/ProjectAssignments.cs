using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Access;
using PlaceContext.Application.Membership;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Auth;

public sealed class ProjectAssignments(AppDbContext db, ICurrentUser user, ICurrentTenant tenant,
    IWorkspaceAdministratorReader administrators) : IProjectAssignments
{
    private async Task RequireAdministratorAsync(CancellationToken ct)
    {
        if (!user.IsAuthenticated || !tenant.IsResolved || !await administrators.IsAdministratorAsync(tenant.TenantId, user.UserId, ct))
            throw new UnauthorizedAccessException("Only workspace administrators can assign projects.");
    }

    public async Task<IReadOnlyList<Guid>?> GetAsync(Guid userId, CancellationToken ct = default)
    {
        await RequireAdministratorAsync(ct);
        var member = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException("Member not found.");
        return member.AssignedProjectIds;
    }

    public async Task SaveAsync(Guid userId, IReadOnlyList<Guid>? projectIds, CancellationToken ct = default)
    {
        await RequireAdministratorAsync(ct);
        var member = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException("Member not found.");
        if (projectIds is not null && (member.IsDefaultAdmin || member.IsExternalAdmin || member.Role == "Owner"))
            throw new InvalidOperationException("Workspace administrators and owners retain access to all projects.");
        if (projectIds is not null && (projectIds.Count > 10000 || projectIds.Contains(Guid.Empty)
            || projectIds.Distinct().Count() != projectIds.Count
            || await db.Projects.CountAsync(p => projectIds.Contains(p.Id), ct) != projectIds.Count))
            throw new ArgumentException("Assign only existing projects from this workspace.");
        member.AssignedProjectIds = projectIds?.ToArray();
        await db.SaveChangesAsync(ct);
    }
}
