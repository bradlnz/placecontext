using System.Collections.Concurrent;
using PlaceContext.Application.Agents.Services;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
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
    // Queue pickup is the first hop of every TUI/trigger run — a short drain tick keeps queued runs
    // from idling; the claim query is a cheap indexed SKIP LOCKED select, so 1s is safe.
    private static readonly TimeSpan DrainInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReapInterval = TimeSpan.FromSeconds(30);
    private const int ClaimBatch = 16;
    // The drain loop used to await each claimed run fully before starting the next — one slow run
    // head-of-line-blocked the other up-to-15 already-claimed rows behind it, invisible to other
    // replicas (they'd already skipped these rows via SKIP LOCKED). Bounded parallelism instead: a
    // conservative default that still leaves headroom under the runner's own container/pod limits.
    private const int DrainParallelism = 4;

    // A run only stays "Running" while a live in-process driver executes it — every shard pod is
    // bounded by the job's own TimeoutSeconds. A run still Running beyond that (plus slack) is
    // orphaned or stuck and is marked Failed against ITS OWN declared timeout (default 300s), not
    // a blanket hour. Chains span multiple runs, so they keep a generous fixed grace.
    private const int ReapSlackSeconds = 60;
    private static readonly TimeSpan ChainOrphanGrace = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly Operations.OperationCenter _opCenter;
    private readonly IDataEncryptor _enc;
    private readonly ILogger<TriggerSchedulerService> _log;
    private readonly string _instanceId = $"{Environment.MachineName}:{Guid.NewGuid():N}";
    private readonly int _drainParallelism;
    private static string PendingPurpose => IDataEncryptor.Purpose.PendingRun;

    public TriggerSchedulerService(IServiceScopeFactory scopes, Operations.OperationCenter opCenter,
        IDataEncryptor enc,
        ILogger<TriggerSchedulerService> log,
        // Test-only override for the drain batch's max degree of parallelism; DI never supplies this
        // (no such service is registered), so production always gets the DrainParallelism default.
        int? drainParallelism = null)
    {
        _scopes = scopes;
        _opCenter = opCenter;
        _enc = enc;
        _log = log;
        _drainParallelism = drainParallelism is > 0 ? drainParallelism.Value : DrainParallelism;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        => await Task.WhenAll(ScanLoopAsync(stoppingToken), DrainLoopAsync(stoppingToken), ReapLoopAsync(stoppingToken));

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
                // Tenant lookups are cached for the whole tick — loaded lazily on the first non-empty
                // batch and reused by every batch drained afterwards, so what used to be one DB round
                // trip per run is now at most one per tick (refreshed next tick, so a tenant created
                // mid-tick still resolves within ~DrainInterval via ProcessOneRunAsync's fallback).
                Dictionary<Guid, TenantInfo>? tenantCache = null;
                int drained;
                do { (drained, tenantCache) = await DrainBatchAsync(tenantCache, ct); }
                while (drained > 0);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _log.LogError(ex, "Run-queue drain failed; will retry next interval."); }
        }
        while (await SafeWaitAsync(timer, ct));
    }

    private async Task<(int Count, Dictionary<Guid, TenantInfo>? TenantCache)> DrainBatchAsync(
        Dictionary<Guid, TenantInfo>? tenantCache, CancellationToken ct)
    {
        var claimed = await ClaimAsync(ct);
        if (claimed.Count == 0) return (0, tenantCache);

        tenantCache ??= (await LoadTenantsAsync(ct)).ToDictionary(t => t.Id);

        var processedIds = await RunBatchInParallelAsync(claimed, tenantCache, ct);
        if (processedIds.Count > 0) await DeleteManyAsync(processedIds, ct);

        return (claimed.Count, tenantCache);
    }

    /// <summary>
    /// Executes every claimed run with bounded parallelism (<see cref="_drainParallelism"/>) instead of
    /// the old serial <c>foreach</c> — one slow run no longer head-of-line-blocks the rest of the batch.
    /// Each run gets its OWN <see cref="IServiceScopeFactory.CreateAsyncScope"/> (its own AppDbContext,
    /// dispatcher, repositories) inside <see cref="ProcessOneRunAsync"/> — scopes/DbContexts are never
    /// shared across parallel iterations, since EF's DbContext is not thread-safe. Returns every claimed
    /// run's id (whether it succeeded or failed) so the caller can drop the durable queue rows in one
    /// batched delete instead of one scope+delete per run.
    /// Internal — not private — so tests can drive it directly without the Npgsql claim/delete plumbing.
    /// </summary>
    internal async Task<IReadOnlyCollection<Guid>> RunBatchInParallelAsync(
        IReadOnlyList<ClaimedRun> claimed,
        IReadOnlyDictionary<Guid, TenantInfo> tenantCache,
        CancellationToken ct)
    {
        var processed = new ConcurrentBag<Guid>();
        var options = new ParallelOptions { MaxDegreeOfParallelism = _drainParallelism, CancellationToken = ct };
        await Parallel.ForEachAsync(claimed, options, async (run, token) =>
        {
            try { await ProcessOneRunAsync(run, tenantCache, token); }
            finally { processed.Add(run.Id); }
        });
        return processed;
    }

    /// <summary>Runs a single claimed queue row. Identical error handling/status-transition semantics
    /// to the old serial loop body — only the caller (now parallel) changed.</summary>
    private async Task ProcessOneRunAsync(
        ClaimedRun run, IReadOnlyDictionary<Guid, TenantInfo> tenantCache, CancellationToken ct)
    {
        try
        {
            var tenant = tenantCache.TryGetValue(run.TenantId, out var cached)
                ? cached
                : await FindTenantAsync(run.TenantId, ct); // cache miss (tenant created mid-tick) — fall back to the DB
            if (tenant is null)
            {
                _log.LogWarning("Dropping queued run for unknown tenant {TenantId}.", run.TenantId);
                return;
            }

            CurrentTenant.Set(tenant);
            try
            {
                // Launchpad rows don't run a job — they kick an autonomous agent session.
                // The agent run is detached so it doesn't block a drain-slot for minutes.
                if (await TryKickLaunchpadAsync(run, tenant, ct))
                    return;

                await using var scope = _scopes.CreateAsyncScope();

                // Surface this run in the notifications bell (Track/Mark* — the external-worker
                // shape, like the analytics sweep) so trigger-fired and TUI-queued runs update
                // the pane live, exactly like a portal-started run. The run id is pre-allocated
                // and used as the op's correlation key, so the run-status watcher converges on
                // this entry (and flips it terminal the moment the row commits) instead of
                // waiting for the dispatcher to return behind the slow enrichment.
                var job = await scope.ServiceProvider.GetRequiredService<Domain.Repositories.IJobRepository>()
                    .GetByIdAsync(run.JobId, ct);
                var runId = Guid.NewGuid();
                var op = _opCenter.Track(tenant, job?.ProjectId,
                    $"Run job — {job?.Name ?? "job"} · {run.TriggerName}",
                    $"/observability?run={runId}",
                    correlationKey: RunStatusWatchService.JobRunKey(runId));
                _opCenter.MarkRunning(op.Id);
                try
                {
                    var jobRunner = scope.ServiceProvider.GetRequiredService<IJobRunner>();
                    var result = await jobRunner.RunAsync(run.JobId, run.Payload, runId, ct: ct);
                    if (result.Status == "Failed") _opCenter.MarkFailed(op.Id, "run finished — Failed");
                    else _opCenter.MarkDone(op.Id, $"run finished — {result.Status}");
                }
                catch (Exception ex)
                {
                    _opCenter.MarkFailed(op.Id, ex.Message);
                    throw;
                }
                _log.LogInformation("Trigger '{Trigger}' ran job {JobId} for tenant {Slug}.",
                    run.TriggerName, run.JobId, tenant.Slug);
            }
            finally { CurrentTenant.Clear(); }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { _log.LogError(ex, "Queued job run {JobId} failed.", run.JobId); }
        // NOTE next throughput win (not done here — riskier, deferred): RunJobHandler.cs:149-205 runs
        // its post-completion enrichment (context-append/embed, best-effort) INLINE before returning,
        // and each map shard reinstalls its own workload dependencies. Detaching that enrichment and
        // caching per-shard dependencies would further cut wall-clock per run; out of scope here.
    }

    // ── Loop 3: reap orphaned runs (all replicas; idempotent UPDATE) ────────────────────────────────

    private async Task ReapLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(ReapInterval);
        do
        {
            try { await ReapOrphanedRunsAsync(ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _log.LogError(ex, "Orphaned-run reap failed; will retry next interval."); }
        }
        while (await SafeWaitAsync(timer, ct));
    }

    /// <summary>
    /// Marks runs stuck in <c>Running</c> past their job's own timeout (+slack) as <c>Failed</c> — these were
    /// abandoned when their driving replica died mid-run, so no pod backs them and they would otherwise
    /// stay Running forever. Cross-tenant by design and idempotent, so every replica can run it safely
    /// (the UPDATE is atomic; concurrent runs just no-op on already-reaped rows).
    /// </summary>
    private async Task ReapOrphanedRunsAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        // Each run reaps against its own job's timeout (+slack); runs whose job is gone fall back
        // to the default 1800s bound.
        cmd.CommandText = """
            UPDATE job_runs r
            SET "Status" = 'Failed', "FinishedAt" = now()
            FROM (SELECT r2."Id", COALESCE(j."TimeoutSeconds", 1800) AS timeout_s
                  FROM job_runs r2 LEFT JOIN jobs j ON j."Id" = r2."JobId"
                  WHERE r2."Status" = 'Running') bounds
            WHERE r."Id" = bounds."Id"
              AND r."Status" = 'Running'
              AND r."StartedAt" < now() - make_interval(secs => bounds.timeout_s + @slack)
            """;
        cmd.Parameters.AddWithValue("slack", ReapSlackSeconds);
        var reaped = await cmd.ExecuteNonQueryAsync(ct);
        if (reaped > 0)
            _log.LogWarning("Reaped {Count} orphaned job run(s) stuck in Running → Failed.", reaped);

        // Chain runs are orphaned the same way (the pipeline driver died between stage saves) and
        // would otherwise show Running in the portal forever. Only the run's own status flips; the
        // steps JSON keeps its last honest snapshot of how far the pipeline actually got.
        await using var chainCmd = conn.CreateCommand();
        chainCmd.CommandText = """
            UPDATE chain_runs
            SET "Status" = 'Failed', "FinishedAt" = now()
            WHERE "Status" = 'Running' AND "StartedAt" < now() - make_interval(secs => @grace)
            """;
        chainCmd.Parameters.AddWithValue("grace", (int)ChainOrphanGrace.TotalSeconds);
        var reapedChains = await chainCmd.ExecuteNonQueryAsync(ct);
        if (reapedChains > 0)
            _log.LogWarning("Reaped {Count} orphaned chain run(s) stuck in Running → Failed.", reapedChains);
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
            RETURNING p."Id", p."TenantId", p."JobId", p."TriggerId", p."TriggerName", p."Payload"
            """;
        cmd.Parameters.AddWithValue("me", _instanceId);
        cmd.Parameters.AddWithValue("batch", ClaimBatch);

        var rows = new List<ClaimedRun>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var cipher = await reader.IsDBNullAsync(5, ct) ? null : reader.GetString(5);
            // Decrypt in Host only — raw DB rows stay opaque (legacy plaintext passthrough).
            var payload = cipher is null ? null : _enc.Unprotect(cipher, PendingPurpose);
            rows.Add(new ClaimedRun(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
                reader.GetGuid(3), reader.GetString(4), payload));
        }
        return rows;
    }

    /// <summary>Drops every drained batch's durable queue rows in one round trip (replaces what used to
    /// be a separate scope+connection+delete per run).</summary>
    private async Task DeleteManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return;
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM pending_job_runs WHERE \"Id\" = ANY(@ids)";
        cmd.Parameters.AddWithValue("ids", ids.ToArray());
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

    /// <summary>Internal (not private) so <c>RunBatchInParallelAsync</c> can be exercised directly by
    /// tests without the Npgsql claim plumbing.</summary>
    internal sealed record ClaimedRun(Guid Id, Guid TenantId, Guid JobId, Guid TriggerId, string TriggerName, string? Payload);

    // ── Launchpad support ──────────────────────────────────────────────────────────────────────────

    private async Task<bool> TryKickLaunchpadAsync(ClaimedRun run, TenantInfo tenant, CancellationToken ct)
    {
        // Launchpad queue rows carry no job id; they are agent-session starts, not job runs.
        if (run.JobId != Guid.Empty) return false;

        await using var scope = _scopes.CreateAsyncScope();
        var trigger = await scope.ServiceProvider.GetRequiredService<Domain.Repositories.IJobTriggerRepository>()
            .GetByIdAsync(run.TriggerId, ct);
        if (trigger is null || trigger.Kind != Domain.ValueObjects.TriggerKind.Launchpad || trigger.ChainId is not { } chainId)
        {
            _log.LogWarning("Launchpad trigger {TriggerId} missing or invalid — dropping queued row.", run.TriggerId);
            return true;
        }

        _log.LogInformation("Kicking launchpad '{Trigger}' for tenant {Slug}.", trigger.Name, tenant.Slug);
        _ = RunLaunchpadDetachedAsync(run.TenantId, tenant, trigger.ProjectId, trigger.Name,
            trigger.Prompt ?? "", trigger.SourceTable, chainId);
        return true;
    }

    private async Task RunLaunchpadDetachedAsync(Guid tenantId, TenantInfo tenant, Guid projectId,
        string triggerName, string prompt, string? sourceTable, Guid chainId)
    {
        try
        {
            CurrentTenant.Set(tenant);
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var runner = scope.ServiceProvider.GetRequiredService<AgentSessionRunner>();
                await runner.RunLaunchpadAsync(projectId, triggerName, prompt, sourceTable, chainId, CancellationToken.None);
            }
            finally { CurrentTenant.Clear(); }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Launchpad '{Trigger}' agent run failed for tenant {Slug}.", triggerName, tenant.Slug);
        }
    }
}
