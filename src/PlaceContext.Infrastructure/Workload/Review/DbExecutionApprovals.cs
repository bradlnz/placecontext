using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Access;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Secrets;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application.Workload;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Workload;

public sealed class DbExecutionApprovals(IServiceScopeFactory scopes, IDataEncryptor encryptor) : IExecutionApprovals
{
    private const string Purpose = "execution.approval.v1";

    public async Task<bool> RequestAsync(Guid projectId, Guid runId, string warning, string evidence, CancellationToken ct)
    {
        if (projectId == Guid.Empty || runId == Guid.Empty || evidence.Length > 65_536)
            throw new ExecutionSafetyException("A safety approval requires a valid project, run and complete execution evidence.");
        var row = new ExecutionApprovalRow
        {
            Id = Guid.NewGuid(), ProjectId = projectId, RunId = runId, Warning = warning,
            Evidence = encryptor.Protect(evidence, Purpose), CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        };
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.Projects.AnyAsync(x => x.Id == projectId, ct))
                throw new UnauthorizedAccessException("Project access is required.");
            db.Set<ExecutionApprovalRow>().Add(row);
            await db.SaveChangesAsync(ct);
        }
        try
        {
            while (DateTimeOffset.UtcNow < row.ExpiresAt)
            {
                ct.ThrowIfCancellationRequested();
                await using var scope = scopes.CreateAsyncScope();
                var current = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
                    .Set<ExecutionApprovalRow>().AsNoTracking().SingleAsync(x => x.Id == row.Id, ct);
                if (current.Status != "Pending") return current.Status == "Approved";
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
            return false;
        }
        finally
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<ExecutionApprovalRow>()
                .Where(x => x.Id == row.Id && x.Status == "Pending")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ct.IsCancellationRequested ? "Cancelled" : "Expired"), CancellationToken.None);
        }
    }

    public async Task<IReadOnlyList<ExecutionApproval>> ListAsync(Guid projectId, IReadOnlyList<Guid> runIds, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        await RequireAsync(scope.ServiceProvider, Permission.JobsView, ct);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.EnsureProjectAccessAsync(projectId, ct);
        var rows = await db.Set<ExecutionApprovalRow>().AsNoTracking()
            .Where(x => x.ProjectId == projectId && runIds.Contains(x.RunId)).OrderBy(x => x.CreatedAt).ToListAsync(ct);
        return rows.Select(x => new ExecutionApproval(x.Id, x.RunId, x.Warning,
            encryptor.Unprotect(x.Evidence, Purpose), x.Status == "Pending" && x.ExpiresAt <= DateTimeOffset.UtcNow ? "Expired" : x.Status,
            x.ExpiresAt, x.DecidedBy)).ToArray();
    }

    public async Task DecideAsync(Guid id, bool approve, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        await RequireAsync(scope.ServiceProvider, Permission.JobsManage, ct);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Set<ExecutionApprovalRow>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new UnauthorizedAccessException("This approval is unavailable.");
        await db.EnsureProjectAccessAsync(row.ProjectId, ct);
        var user = scope.ServiceProvider.GetRequiredService<ICurrentUser>();
        var changed = await db.Set<ExecutionApprovalRow>()
            .Where(x => x.Id == id && x.Status == "Pending" && x.ExpiresAt > DateTimeOffset.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, approve ? "Approved" : "Rejected")
                .SetProperty(x => x.DecidedBy, user.UserId).SetProperty(x => x.DecidedAt, DateTimeOffset.UtcNow), ct);
        if (changed == 0) throw new InvalidOperationException("This request has already been answered or has expired.");
    }

    private static async Task RequireAsync(IServiceProvider services, string permission, CancellationToken ct)
    {
        if (!services.GetRequiredService<ICurrentTenant>().IsResolved || !services.GetRequiredService<ICurrentUser>().IsAuthenticated
            || !await services.GetRequiredService<IPermissionService>().HasAsync(permission, ct))
            throw new UnauthorizedAccessException("You do not have permission to review this execution.");
    }
}
