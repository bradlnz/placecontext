using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Access;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Build;
using PlaceContext.Application.Secrets;
using PlaceContext.Application.Shared;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application.Workload.VirtualMachines;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Build;

public sealed class WorkflowDemoShares(IServiceScopeFactory scopes, ICurrentTenant tenant, ICurrentUser user,
    IPermissionService permissions, IDataEncryptor encryption, IVirtualMachineReplayStore replay, IClock clock) : IWorkflowDemoShares
{
    private const string Purpose = "workflow.demo.v1";

    private async Task CheckAsync(AppDbContext db, Guid projectId, Guid runId, CancellationToken ct)
    {
        if (!tenant.IsResolved || !user.IsAuthenticated || !await permissions.HasAsync(Permission.ProjectsView, ct)
            || !await permissions.HasAsync(Permission.ChainsManage, ct) || !await permissions.HasAsync(Permission.JobsView, ct)
            || !await permissions.HasAsync(Permission.ArtifactsShare, ct))
        {
            throw new UnauthorizedAccessException("Workflow viewing and public sharing permissions are required.");
        }
        if (!await db.ChainRuns.AnyAsync(x => x.Id == runId && x.ProjectId == projectId, ct))
        {
            throw new InvalidOperationException("Workflow run not found in this project.");
        }
    }

    public async Task<WorkflowDemoLink> CreateAsync(Guid projectId, Guid runId, string outcome, int? replayStep, int lifetimeDays, CancellationToken ct = default)
    {
        if (outcome is null || outcome.Length > 250_000 || lifetimeDays is < 1 or > 30)
        {
            throw new ArgumentException("Outcome must be at most 250,000 characters. Links must expire in 1–30 days.");
        }
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CheckAsync(db, projectId, runId, ct);
        var run = await new EfChainRunRepository(db, encryption).GetByIdAsync(runId, ct)
            ?? throw new InvalidOperationException("Workflow run not found.");
        if (run.FinishedAt is null)
        {
            throw new InvalidOperationException("Wait for the run to finish before sharing a fixed demo snapshot.");
        }
        var frames = new List<WorkflowDemoFrame>();
        long imageBytes = 0;
        if (replayStep is { } step)
        {
            if (!run.Steps.Any(x => x.Index == step && x.ActionType == VirtualMachineChainAction.ActionType))
            {
                throw new ArgumentException("Select a recorded browser workspace step.");
            }
            foreach (var frame in await replay.ListAsync(projectId, runId, step, ct))
            {
                var image = frame.HasImage ? await replay.ReadImageAsync(projectId, runId, step, frame.Sequence, ct) : null;
                imageBytes += image?.Length ?? 0;
                if (frames.Count >= IVirtualMachineReplayStore.MaxFrames || imageBytes > IVirtualMachineReplayStore.MaxReplayBytes)
                {
                    throw new InvalidOperationException("This replay exceeds the demo size limit.");
                }
                frames.Add(new(frame.Description, frame.ElapsedMilliseconds, image is null ? null : Convert.ToBase64String(image)));
            }
            if (!frames.Any(x => x.Image is not null))
            {
                throw new InvalidOperationException("No recorded screenshots are available for this step.");
            }
        }
        if (string.IsNullOrWhiteSpace(outcome) && frames.Count == 0)
        {
            throw new ArgumentException("Include an outcome or a visual replay.");
        }
        // Recheck access after reading images, before minting the public bearer credential.
        await CheckAsync(db, projectId, runId, ct);
        var token = "pc_demo_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var now = clock.UtcNow;
        var expires = now.AddDays(lifetimeDays);
        var row = await db.WorkflowDemoShares.SingleOrDefaultAsync(x => x.UserId == user.UserId && x.RunId == runId, ct);
        if (row is null)
        {
            row = new() { UserId = user.UserId, RunId = runId, ProjectId = projectId };
            db.WorkflowDemoShares.Add(row);
        }
        row.TokenHash = Hash(token);
        row.ExpiresAt = expires;
        row.Content = encryption.Protect(JsonSerializer.Serialize(new WorkflowDemo(run.ChainName, run.Status.ToString(), outcome, now, expires, frames)), Purpose);
        await db.SaveChangesAsync(ct);
        return new(token, expires);
    }

    public async Task<DateTimeOffset?> GetExpiryAsync(Guid projectId, Guid runId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CheckAsync(db, projectId, runId, ct);
        return await db.WorkflowDemoShares.Where(x => x.RunId == runId && x.UserId == user.UserId && x.ExpiresAt > clock.UtcNow)
            .Select(x => (DateTimeOffset?)x.ExpiresAt).SingleOrDefaultAsync(ct);
    }

    public async Task RevokeAsync(Guid projectId, Guid runId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CheckAsync(db, projectId, runId, ct);
        var row = await db.WorkflowDemoShares.SingleOrDefaultAsync(x => x.UserId == user.UserId && x.RunId == runId, ct);
        if (row is not null)
        {
            db.WorkflowDemoShares.Remove(row);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<WorkflowDemo?> ResolveAsync(string token, CancellationToken ct = default)
    {
        if (token.Length != 72 || !token.StartsWith("pc_demo_", StringComparison.Ordinal))
        {
            return null;
        }
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hash = Hash(token);
        var row = await db.WorkflowDemoShares.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.TokenHash == hash && x.ExpiresAt > clock.UtcNow, ct);
        if (row is null || !await db.ChainRuns.IgnoreQueryFilters().AnyAsync(x => x.Id == row.RunId
            && x.TenantId == row.TenantId && x.ProjectId == row.ProjectId, ct))
        {
            return null;
        }
        return JsonSerializer.Deserialize<WorkflowDemo>(encryption.Unprotect(row.Content, Purpose));
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
