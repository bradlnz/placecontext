using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Access;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Billing;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Tenancy;

public sealed class CloudWorkspace(IServiceScopeFactory scopes, ICurrentTenant tenant,
    IPermissionService permissions, IOptions<RunBillingDeploymentOptions> options) : ICloudWorkspace
{
    private async Task CheckAsync(CancellationToken ct)
    {
        if (!options.Value.ManagedHosting || !tenant.IsResolved || tenant.TenantId == Guid.Empty
            || !await permissions.HasAsync(Permission.SettingsManage, ct))
            throw new UnauthorizedAccessException("Workspace administration is required.");
    }

    public async Task<CloudWorkspaceStatus?> GetAsync(CancellationToken ct = default)
    {
        await CheckAsync(ct);
        await using var scope = scopes.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .CloudWorkspaces.AsNoTracking().SingleOrDefaultAsync(ct);
        return row is null ? null : new(row.State, row.Plan, row.Attempts,
            row.State == "failed" && row.Attempts < 5 ? row.NextAttemptAt : null);
    }

    public async Task RetryAsync(CancellationToken ct = default)
    {
        await CheckAsync(ct);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.CloudWorkspaces.Where(x => x.State == "failed" && x.Attempts >= 5)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "pending")
                .SetProperty(x => x.Attempts, 0).SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow), ct);
    }
}
