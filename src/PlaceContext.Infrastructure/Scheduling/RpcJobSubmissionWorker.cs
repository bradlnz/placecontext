using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using PlaceContext.Application;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Infrastructure.Scheduling;

/// <summary>Replicated durable worker for standalone API jobs. A crashed pre-start claim is
/// recovered using the same run id; an already persisted run is never dispatched twice.</summary>
public sealed class RpcJobSubmissionWorker(
    IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<RpcJobSubmissionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);
    private readonly int _maxConcurrent = Math.Clamp(
        configuration.GetValue<int?>("PlaceContext:Scheduling:MaxConcurrentApiJobs") ?? 4, 1, 64);
    private readonly string _instanceId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var active = new List<Task>();
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            do
            {
                try
                {
                    foreach (var completed in active.Where(t => t.IsCompleted).ToArray())
                    {
                        active.Remove(completed);
                        await completed;
                    }
                    var available = _maxConcurrent - active.Count;
                    if (available > 0)
                        active.AddRange((await ClaimAsync(available, ct))
                            .Select(submission => ProcessAsync(submission, ct)));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogError(ex, "Managed job submission drain failed."); }
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            try { await Task.WhenAll(active); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        }
    }

    /// <summary>Reconcile completed jobs after worker crashes without replaying established runs.</summary>
    internal static Task<int> ReconcileFinishedAsync(AppDbContext db, CancellationToken ct = default)
        => db.Database.ExecuteSqlRawAsync("""
            UPDATE rpc_job_submissions s
            SET "Status" = j."Status", "FinishedAt" = now(),
                "ClaimedBy" = NULL, "ClaimedAt" = NULL, "HeartbeatAt" = NULL,
                "LastError" = NULL
            FROM job_runs j
            WHERE s."TenantId" = j."TenantId" AND s."RunId" = j."Id"
              AND j."Status" IN ('Succeeded', 'Partial', 'Failed', 'Cancelled')
              AND s."Status" IN ('Queued', 'Running')
            """, ct);

    private async Task<IReadOnlyList<Claimed>> ClaimAsync(int capacity, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        await ReconcileFinishedAsync(db, ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE rpc_job_submissions s
            SET "Status" = 'Running', "ClaimedBy" = @worker,
                "ClaimedAt" = now(), "HeartbeatAt" = now(),
                "StartedAt" = COALESCE("StartedAt", now())
            FROM (
                SELECT candidate."Id" FROM rpc_job_submissions candidate
                WHERE (("Status" = 'Queued' AND "NextAttemptAt" <= now())
                   OR ("Status" = 'Running' AND "HeartbeatAt" < now() - interval '2 minutes'))
                  AND NOT EXISTS (
                      SELECT 1 FROM job_runs established
                      WHERE established."TenantId" = candidate."TenantId"
                        AND established."Id" = candidate."RunId"
                  )
                ORDER BY candidate."SubmittedAt"
                FOR UPDATE OF candidate SKIP LOCKED
                LIMIT @capacity
            ) c
            WHERE s."Id" = c."Id"
            RETURNING s."Id", s."TenantId", s."ProjectId", s."JobId",
                      s."RunId", s."InputPayload"
            """;
        cmd.Parameters.AddWithValue("worker", _instanceId);
        cmd.Parameters.AddWithValue("capacity", capacity);
        var rows = new List<Claimed>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new Claimed(reader.GetGuid(0), reader.GetGuid(1),
                reader.GetGuid(2), reader.GetGuid(3), reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        return rows;
    }

    private async Task ProcessAsync(Claimed run, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == run.TenantId, ct)
                ?? throw new InvalidOperationException("Submitting tenant no longer exists.");
            CurrentTenant.Set(new TenantInfo(tenant.Id, tenant.Slug, tenant.Name, tenant.TimeZoneId));
            try
            {
                var service = scope.ServiceProvider.GetRequiredService<PlaceContextService>();
                var existing = await service.GetJobRunAsync(run.RunId, ct);
                if (existing is not null)
                {
                    await FinishAsync(run, existing.Status, ct);
                    return;
                }
                var encryptor = scope.ServiceProvider.GetRequiredService<IDataEncryptor>();
                var input = run.ProtectedPayload is null ? null
                    : encryptor.Unprotect(run.ProtectedPayload, IDataEncryptor.Purpose.PendingRun);
                var runner = scope.ServiceProvider.GetRequiredService<IJobRunner>();
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var heartbeat = HeartbeatAsync(run.Id, linked.Token);
                try
                {
                    var result = await runner.RunAsync(run.JobId, input, run.RunId, ct: ct);
                    await FinishAsync(run, result.Status, ct);
                }
                finally
                {
                    await linked.CancelAsync();
                    try { await heartbeat; }
                    catch (OperationCanceledException) { }
                }
            }
            finally { CurrentTenant.Clear(); }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Never dispatch a second time once the run row exists.
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == run.TenantId, ct);
                if (tenant is not null)
                {
                    CurrentTenant.Set(new TenantInfo(tenant.Id, tenant.Slug, tenant.Name, tenant.TimeZoneId));
                    var existing = await scope.ServiceProvider.GetRequiredService<PlaceContextService>()
                        .GetJobRunAsync(run.RunId, ct);
                    if (existing is not null)
                    {
                        await FinishAsync(run, existing.Status, ct);
                        return;
                    }
                }
            }
            catch (Exception lookup)
            {
                logger.LogWarning(lookup, "Could not reconcile managed job {RunId}.", run.RunId);
            }
            finally { CurrentTenant.Clear(); }
            await RetryOrFailAsync(run, ex.Message, ct);
            logger.LogError(ex, "Managed job {JobId} dispatch failed.", run.JobId);
        }
    }

    private async Task FinishAsync(Claimed run, string status, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE rpc_job_submissions
            SET "Status" = {status}, "HeartbeatAt" = now(),
                "FinishedAt" = CASE WHEN {status} IN ('Succeeded','Partial','Failed','Cancelled')
                    THEN now() ELSE NULL END,
                "ClaimedBy" = NULL, "ClaimedAt" = NULL
            WHERE "Id" = {run.Id} AND "TenantId" = {run.TenantId}
              AND "RunId" = {run.RunId} AND "ClaimedBy" = {_instanceId}
            """, ct);
    }

    private async Task RetryOrFailAsync(Claimed run, string message, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var encryptor = scope.ServiceProvider.GetRequiredService<IDataEncryptor>();
        var error = encryptor.Protect(message[..Math.Min(message.Length, 500)],
            IDataEncryptor.Purpose.PendingRun);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE rpc_job_submissions
            SET "Attempts" = "Attempts" + 1,
                "Status" = CASE WHEN "Attempts" + 1 >= 3 THEN 'Failed' ELSE 'Queued' END,
                "LastError" = {error}, "ClaimedBy" = NULL, "ClaimedAt" = NULL,
                "HeartbeatAt" = NULL,
                "NextAttemptAt" = now() + interval '10 seconds',
                "FinishedAt" = CASE WHEN "Attempts" + 1 >= 3 THEN now() ELSE NULL END
            WHERE "Id" = {run.Id} AND "TenantId" = {run.TenantId}
              AND "RunId" = {run.RunId} AND "ClaimedBy" = {_instanceId}
            """, ct);
    }

    private async Task HeartbeatAsync(Guid id, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE rpc_job_submissions SET "HeartbeatAt" = now()
                    WHERE "Id" = {id} AND "ClaimedBy" = {_instanceId}
                    """, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogWarning(ex, "Could not heartbeat job {Id}.", id); }
        }
    }

    private sealed record Claimed(Guid Id, Guid TenantId, Guid ProjectId,
        Guid JobId, Guid RunId, string? ProtectedPayload);
}
