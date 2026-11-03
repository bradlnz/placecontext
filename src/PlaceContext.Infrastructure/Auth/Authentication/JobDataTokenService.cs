using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Access;
using PlaceContext.Application.Jobs;
using PlaceContext.Application.Shared;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Auth;

public sealed class JobDataTokenService(
    AppDbContext db, IDataProtectionProvider protection, ICurrentTenant tenant,
    IClock clock, IPermissionService permissions) : IJobDataTokenService
{
    private const string Prefix = "pc_job_";
    private readonly IDataProtector _protector = protection.CreateProtector("placecontext.job-data.v1");

    public async Task<string> CreateAsync(Guid runId, CancellationToken ct = default)
    {
        if (!await permissions.HasAsync(Permission.DataRead, ct))
        {
            throw new UnauthorizedAccessException("Starting a job with project data access requires data.read.");
        }
        var cutoff = clock.UtcNow.AddSeconds(-IJobRunHeartbeat.TimeoutSeconds);
        var run = await db.JobRuns.AsNoTracking()
            .Where(row => row.Id == runId && row.Status == "Running" && row.FinishedAt == null
                && (row.HeartbeatAt ?? row.StartedAt) > cutoff)
            .Select(row => new { row.TenantId, row.ProjectId, row.JobId, row.Id })
            .SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Project data access requires an active run.");
        var grant = new JobDataGrant(run.TenantId, run.ProjectId, run.JobId, run.Id, clock.UtcNow.AddHours(24));
        return Prefix + _protector.Protect(JsonSerializer.Serialize(grant));
    }

    public async Task<JobDataGrant?> ValidateAsync(string token, CancellationToken ct = default)
    {
        if (!tenant.IsResolved || tenant.TenantId == Guid.Empty
            || string.IsNullOrEmpty(token) || token.Length > 4096
            || !token.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }
        JobDataGrant? grant;
        try
        {
            grant = JsonSerializer.Deserialize<JobDataGrant>(_protector.Unprotect(token[Prefix.Length..]));
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        if (grant is null || grant.TenantId != tenant.TenantId || grant.ExpiresAt <= clock.UtcNow)
        {
            return null;
        }
        var cutoff = clock.UtcNow.AddSeconds(-IJobRunHeartbeat.TimeoutSeconds);
        var active = await db.JobRuns.AsNoTracking().AnyAsync(row =>
            row.Id == grant.RunId && row.TenantId == grant.TenantId
            && row.ProjectId == grant.ProjectId && row.JobId == grant.JobId
            && row.Status == "Running" && row.FinishedAt == null
            && (row.HeartbeatAt ?? row.StartedAt) > cutoff, ct);
        return active ? grant : null;
    }
}
