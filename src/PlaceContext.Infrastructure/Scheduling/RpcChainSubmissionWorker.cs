using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using PlaceContext.Application;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Infrastructure.Scheduling;

/// <summary>
/// Drains asynchronous MCP chain submissions. Claiming is atomic across replicas, active claims are
/// heartbeated, and a stale claim is only dispatched again when its pre-allocated chain run does not
/// exist. That closes the crash-before-start window without ever duplicating an established run.
/// </summary>
public sealed class RpcChainSubmissionWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);
    private const int BatchSize = 2;
    private const int MaxAttempts = 3;
    private static readonly TimeSpan TrackingRetention = TimeSpan.FromDays(30);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<RpcChainSubmissionWorker> _log;
    private readonly string _instanceId = $"{Environment.MachineName}:{Guid.NewGuid():N}";
    private DateTimeOffset _nextCleanupAt = DateTimeOffset.MinValue;

    public RpcChainSubmissionWorker(
        IServiceScopeFactory scopes,
        ILogger<RpcChainSubmissionWorker> log)
        => (_scopes, _log) = (scopes, log);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                var submissions = await ClaimAsync(stoppingToken);
                await Task.WhenAll(submissions.Select(value => ProcessAsync(value, stoppingToken)));
                if (DateTimeOffset.UtcNow >= _nextCleanupAt)
                    await CleanupTrackingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _log.LogError(ex, "RPC chain submission drain failed."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<IReadOnlyList<ClaimedSubmission>> ClaimAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE rpc_chain_submissions submission
            SET "Status" = 'Running', "ClaimedBy" = @worker, "ClaimedAt" = now(),
                "HeartbeatAt" = now(), "StartedAt" = COALESCE("StartedAt", now())
            FROM (
                SELECT "Id"
                FROM rpc_chain_submissions
                WHERE (
                    ("Status" = 'Queued' AND "NextAttemptAt" <= now())
                    OR ("Status" = 'Running' AND "HeartbeatAt" < now() - interval '2 minutes')
                )
                ORDER BY "SubmittedAt"
                FOR UPDATE SKIP LOCKED
                LIMIT @batch
            ) candidate
            WHERE submission."Id" = candidate."Id"
            RETURNING submission."Id", submission."TenantId", submission."ProjectId",
                      submission."ChainId", submission."ChainRunId", submission."InputPayload",
                      submission."SubmitterUserId", submission."SubmitterRole"
            """;
        command.Parameters.AddWithValue("worker", _instanceId);
        command.Parameters.AddWithValue("batch", BatchSize);

        var values = new List<ClaimedSubmission>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            values.Add(new ClaimedSubmission(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3),
                reader.GetGuid(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetGuid(6), reader.GetString(7)));
        }
        return values;
    }

    private async Task ProcessAsync(ClaimedSubmission submission, CancellationToken ct)
    {
        try
        {
            var tenant = await LoadTenantAsync(submission.TenantId, ct)
                ?? throw new InvalidOperationException($"Tenant {submission.TenantId} no longer exists.");
            CurrentTenant.Set(tenant);
            var submitter = await LoadSubmitterAsync(
                submission.TenantId, submission.SubmitterUserId, ct)
                ?? throw new InvalidOperationException(
                    $"Submitting user {submission.SubmitterUserId} no longer exists.");
            CurrentUser.Set(submitter);
            try
            {
                // A stale claim may mean the original worker died, or merely lost its heartbeat.
                // Never invoke the handler twice once its authoritative chain-run row exists.
                var existing = await LoadRunAsync(submission.ChainRunId, ct);
                if (existing is not null)
                {
                    await ReflectRunStatusAsync(submission.Id, existing, ct);
                    return;
                }

                await using var scope = _scopes.CreateAsyncScope();
                var encryptor = scope.ServiceProvider.GetRequiredService<IDataEncryptor>();
                var payload = submission.ProtectedPayload is null
                    ? null
                    : encryptor.Unprotect(
                        submission.ProtectedPayload,
                        IDataEncryptor.Purpose.RpcChainSubmission);
                var handler = scope.ServiceProvider.GetRequiredService<
                    ICommandHandler<RunJobChainCommand, ChainRunView>>();

                using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var heartbeat = HeartbeatAsync(submission.Id, heartbeatCts.Token);
                ChainRunView result;
                try
                {
                    result = await handler.HandleAsync(new RunJobChainCommand(
                        submission.ChainId, payload, submission.ChainRunId), ct);
                }
                finally
                {
                    await heartbeatCts.CancelAsync();
                    try { await heartbeat; }
                    catch (OperationCanceledException) { }
                }

                await ReflectRunStatusAsync(submission.Id, result, ct);
                _log.LogInformation(
                    "RPC submission {TrackingId} ran chain {ChainId} as {ChainRunId} ({Status}).",
                    submission.Id, submission.ChainId, submission.ChainRunId, result.Status);
            }
            finally
            {
                CurrentUser.Clear();
                CurrentTenant.Clear();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // If the handler established the run before failing, retrying the same id would either
            // duplicate work or hit a primary-key collision. Preserve and expose that run instead.
            ChainRunView? existing = null;
            try
            {
                var tenant = await LoadTenantAsync(submission.TenantId, ct);
                if (tenant is not null)
                {
                    CurrentTenant.Set(tenant);
                    existing = await LoadRunAsync(submission.ChainRunId, ct);
                }
            }
            finally { CurrentTenant.Clear(); }

            if (existing is not null)
                await ReflectRunStatusAsync(submission.Id, existing, ct);
            else
                await ReleaseOrFailAsync(submission.Id, ex.Message, ct);
            _log.LogError(ex, "RPC chain submission {TrackingId} failed.", submission.Id);
        }
    }

    private async Task HeartbeatAsync(Guid id, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE rpc_chain_submissions SET "HeartbeatAt" = now()
                    WHERE "Id" = {id} AND "ClaimedBy" = {_instanceId}
                    """, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not heartbeat RPC chain submission {TrackingId}.", id);
            }
        }
    }

    private async Task<TenantInfo?> LoadTenantAsync(Guid id, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : new TenantInfo(row.Id, row.Slug, row.Name, row.TimeZoneId);
    }

    private async Task<UserIdentity?> LoadSubmitterAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(value => value.TenantId == tenantId && value.Id == userId, ct);
        return row is null ? null : new UserIdentity(row.Id, row.Role);
    }

    private async Task<ChainRunView?> LoadRunAsync(Guid runId, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPlaceContextService>()
            .GetChainRunAsync(runId, ct);
    }

    private async Task ReflectRunStatusAsync(Guid id, ChainRunView run, CancellationToken ct)
    {
        var terminal = run.Status is "Succeeded" or "Partial" or "Failed" or "Cancelled";
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE rpc_chain_submissions
            SET "Status" = {run.Status}, "ClaimedBy" = NULL, "ClaimedAt" = NULL,
                "HeartbeatAt" = now(), "FinishedAt" = {run.FinishedAt},
                "LastError" = NULL,
                "InputPayload" = CASE WHEN {terminal} THEN NULL ELSE "InputPayload" END
            WHERE "Id" = {id}
            """, ct);
    }

    private async Task CleanupTrackingAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(TrackingRetention);
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var removed = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM rpc_chain_submissions submission
            WHERE submission."FinishedAt" < {cutoff}
               OR EXISTS (
                    SELECT 1 FROM chain_runs run
                    WHERE run."Id" = submission."ChainRunId"
                      AND run."FinishedAt" < {cutoff}
               )
            """, ct);
        _nextCleanupAt = DateTimeOffset.UtcNow.Add(CleanupInterval);
        if (removed > 0)
            _log.LogInformation("Removed {Count} expired RPC chain submission receipts.", removed);
    }

    private async Task ReleaseOrFailAsync(Guid id, string error, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var encryptor = scope.ServiceProvider.GetRequiredService<IDataEncryptor>();
        var bounded = error.Length <= 1000 ? error : error[..1000];
        var protectedError = encryptor.Protect(bounded, IDataEncryptor.Purpose.RpcChainSubmission);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE rpc_chain_submissions
            SET "Attempts" = "Attempts" + 1,
                "Status" = CASE WHEN "Attempts" + 1 >= {MaxAttempts} THEN 'Failed' ELSE 'Queued' END,
                "LastError" = {protectedError}, "ClaimedBy" = NULL, "ClaimedAt" = NULL,
                "HeartbeatAt" = NULL,
                "NextAttemptAt" = now() + make_interval(secs => power(3, "Attempts" + 1)::integer),
                "FinishedAt" = CASE WHEN "Attempts" + 1 >= {MaxAttempts} THEN now() ELSE NULL END,
                "InputPayload" = CASE WHEN "Attempts" + 1 >= {MaxAttempts} THEN NULL ELSE "InputPayload" END
            WHERE "Id" = {id}
            """, ct);
    }

    private sealed record ClaimedSubmission(
        Guid Id,
        Guid TenantId,
        Guid ProjectId,
        Guid ChainId,
        Guid ChainRunId,
        string? ProtectedPayload,
        Guid SubmitterUserId,
        string SubmitterRole);
}
