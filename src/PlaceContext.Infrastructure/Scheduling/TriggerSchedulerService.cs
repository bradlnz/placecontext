using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace PlaceContext.Infrastructure.Scheduling;

/// <summary>
/// Background worker that drives triggers across k3s replicas. Two concurrent loops:
/// <list type="number">
/// <item><b>Scan</b> — fires due cron schedules for every tenant. Guarded by a Postgres advisory lock
/// so exactly one replica scans at a time (no duplicate firings); environment-agnostic leader election
/// that needs no Kubernetes API/RBAC.</item>
/// <item><b>Drain</b> — claims queued runs from <c>pending_job_runs</c> with <c>FOR UPDATE SKIP LOCKED</c>
/// (so any/all replicas can drain safely) and executes each, re-establishing the ambient tenant first.</item>
/// </list>
/// The queue is durable, so runs survive restarts and are never lost when the producing replica differs
/// from the executing one.
/// </summary>
public sealed class TriggerSchedulerService : BackgroundService
{
    // Arbitrary fixed key identifying the schedule-scan advisory lock.
    private const long ScanLockKey = 0x504C4143_4378_7363L; // "PLAC...sc"
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DrainInterval = TimeSpan.FromSeconds(3);
    private const int ClaimBatch = 16;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<TriggerSchedulerService> _log;
    private readonly string _instanceId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public TriggerSchedulerService(IServiceScopeFactory scopes, ILogger<TriggerSchedulerService> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        => await Task.WhenAll(ScanLoopAsync(stoppingToken), DrainLoopAsync(stoppingToken));

    // ── Loop 1: fire due schedules (single leader via advisory lock) ───────────────────────────────

    private async Task ScanLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(ScanInterval);
        do
        {
            try { await ScanOnceAsLeaderAsync(ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _log.LogError(ex, "Schedule scan failed; will retry next interval."); }
        }
        while (await SafeWaitAsync(timer, ct));
    }

    private async Task ScanOnceAsLeaderAsync(CancellationToken ct)
    {
        await using var lockScope = _scopes.CreateAsyncScope();
        var db = lockScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);

        if (!await TryAdvisoryLockAsync(conn, ct)) return; // another replica is the leader this tick
        try
        {
            foreach (var tenant in await LoadTenantsAsync(ct))
            {
                CurrentTenant.Set(tenant);
                try
                {
                    await using var scope = _scopes.CreateAsyncScope();
                    var fired = await scope.ServiceProvider.GetRequiredService<ScheduleScanService>().FireDueAsync(ct);
                    if (fired > 0)
                        _log.LogInformation("Fired {Count} schedule trigger(s) for tenant {Slug}.", fired, tenant.Slug);
                }
                finally { CurrentTenant.Clear(); }
            }
        }
        finally
        {
            await ExecScalarAsync(conn, "SELECT pg_advisory_unlock(@key)", ct);
        }
    }

    // ── Loop 2: drain the durable run queue (all replicas, atomic claiming) ─────────────────────────

    private async Task DrainLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(DrainInterval);
        do
        {
            try
            {
                // Drain greedily until nothing is left, then wait for the next tick.
                while (await DrainBatchAsync(ct) > 0) { }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _log.LogError(ex, "Run-queue drain failed; will retry next interval."); }
        }
        while (await SafeWaitAsync(timer, ct));
    }

    private async Task<int> DrainBatchAsync(CancellationToken ct)
    {
        var claimed = await ClaimAsync(ct);
        foreach (var run in claimed)
        {
            try
            {
                var tenant = await FindTenantAsync(run.TenantId, ct);
                if (tenant is null)
                {
                    _log.LogWarning("Dropping queued run for unknown tenant {TenantId}.", run.TenantId);
                }
                else
                {
                    CurrentTenant.Set(tenant);
                    try
                    {
                        await using var scope = _scopes.CreateAsyncScope();
                        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
                        await dispatcher.Send(new RunJobCommand(run.JobId, run.Payload), ct);
                        _log.LogInformation("Trigger '{Trigger}' ran job {JobId} for tenant {Slug}.",
                            run.TriggerName, run.JobId, tenant.Slug);
                    }
                    finally { CurrentTenant.Clear(); }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _log.LogError(ex, "Queued job run {JobId} failed.", run.JobId); }
            finally { await DeleteAsync(run.Id, ct); }
        }
        return claimed.Count;
    }

    private async Task<List<ClaimedRun>> ClaimAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        // Columns are EF's default quoted PascalCase identifiers.
        cmd.CommandText = """
            UPDATE pending_job_runs p
            SET "ClaimedBy" = @me, "ClaimedAt" = now()
            FROM (
                SELECT "Id" FROM pending_job_runs
                WHERE "ClaimedAt" IS NULL
                ORDER BY "EnqueuedAt"
                FOR UPDATE SKIP LOCKED
                LIMIT @batch
            ) c
            WHERE p."Id" = c."Id"
            RETURNING p."Id", p."TenantId", p."JobId", p."TriggerName", p."Payload"
            """;
        cmd.Parameters.AddWithValue("me", _instanceId);
        cmd.Parameters.AddWithValue("batch", ClaimBatch);

        var rows = new List<ClaimedRun>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new ClaimedRun(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
                reader.GetString(3), await reader.IsDBNullAsync(4, ct) ? null : reader.GetString(4)));
        return rows;
    }

    private async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM pending_job_runs WHERE \"Id\" = @id";
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────

    private async Task<bool> TryAdvisoryLockAsync(NpgsqlConnection conn, CancellationToken ct)
        => await ExecScalarAsync(conn, "SELECT pg_try_advisory_lock(@key)", ct) is true;

    private static async Task<object?> ExecScalarAsync(NpgsqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("key", ScanLockKey);
        return await cmd.ExecuteScalarAsync(ct);
    }

    private async Task<IReadOnlyList<TenantInfo>> LoadTenantsAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tenants.AsNoTracking()
            .Select(t => new TenantInfo(t.Id, t.Slug, t.Name, t.TimeZoneId)).ToListAsync(ct);
    }

    private async Task<TenantInfo?> FindTenantAsync(Guid tenantId, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new TenantInfo(t.Id, t.Slug, t.Name, t.TimeZoneId))
            .FirstOrDefaultAsync(ct);
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    private sealed record ClaimedRun(Guid Id, Guid TenantId, Guid JobId, string TriggerName, string? Payload);
}
