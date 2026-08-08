using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Automation;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Dtos;
using PlaceContext.Crm.Infrastructure.Persistence;

namespace PlaceContext.Crm.Infrastructure.Scheduling;

/// <summary>Durably drains lifecycle-event automation rules without blocking the CRM request.</summary>
public sealed class CrmAutomationWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private const int BatchSize = 4;
    private const int MaxAttempts = 3;
    private static readonly TimeSpan TrackingRetention = TimeSpan.FromDays(30);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
    private readonly IServiceScopeFactory _scopes;
    private readonly ICurrentTenantAccessor _tenantAccessor;
    private readonly ILogger<CrmAutomationWorker> _log;
    private readonly string _instanceId = $"{Environment.MachineName}:{Guid.NewGuid():N}";
    private DateTimeOffset _nextCleanupAt = DateTimeOffset.MinValue;

    public CrmAutomationWorker(
        IServiceScopeFactory scopes,
        ICurrentTenantAccessor tenantAccessor,
        ILogger<CrmAutomationWorker> log)
        => (_scopes, _tenantAccessor, _log) = (scopes, tenantAccessor, log);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                var claimed = await ClaimAsync(stoppingToken);
                foreach (var item in claimed)
                    await ProcessAsync(item, stoppingToken);
                if (DateTimeOffset.UtcNow >= _nextCleanupAt)
                    await CleanupTrackingAsync(stoppingToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _log.LogError(ex, "CRM automation queue drain failed."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<IReadOnlyList<CrmAutomationQueueRow>> ClaimAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var rows = await db.CrmAutomationQueue.FromSqlRaw(
                """
                SELECT * FROM crm_automation_queue
                WHERE "CompletedAt" IS NULL
                  AND "FailedAt" IS NULL
                  AND "ClaimedAt" IS NULL
                  AND "NextAttemptAt" <= now()
                ORDER BY "EnqueuedAt"
                FOR UPDATE SKIP LOCKED
                LIMIT 4
                """)
            .ToListAsync(ct);
        foreach (var row in rows)
        {
            row.ClaimedAt = now;
            row.ClaimedBy = _instanceId;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return rows.Select(Clone).ToList();
    }

    private async Task ProcessAsync(CrmAutomationQueueRow item, CancellationToken ct)
    {
        try
        {
            var tenant = await LoadTenantAsync(item.TenantId, ct);
            if (tenant is null) throw new InvalidOperationException($"Tenant {item.TenantId} no longer exists.");
            _tenantAccessor.Set(tenant);
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                Guid chainRunId;
                string resultStatus;
                if (item.InputPayloadProtected is null && item.ClientId is { } clientId)
                {
                    var handler = scope.ServiceProvider.GetRequiredService<
                        ICommandHandler<RunCrmClientAutomationCommand, CrmChainRunView>>();
                    var result = await handler.HandleAsync(
                        new RunCrmClientAutomationCommand(clientId, item.ChainId), ct);
                    chainRunId = result.ChainRunId;
                    resultStatus = result.Status;
                }
                else
                {
                    chainRunId = item.ChainRunId ?? Guid.NewGuid();
                    await MarkRunningAsync(item.Id, chainRunId, ct);
                    var encryptor = scope.ServiceProvider.GetRequiredService<IDataEncryptor>();
                    var handler = scope.ServiceProvider.GetRequiredService<
                        ICommandHandler<RunJobChainCommand, ChainRunView>>();
                    var payload = encryptor.Unprotect(
                        item.InputPayloadProtected, DataEncryptionPurpose.CrmAutomationPayload);
                    var result = await handler.HandleAsync(
                        new RunJobChainCommand(item.ChainId, payload, chainRunId,
                            CrmClientId: item.ClientId), ct);
                    resultStatus = result.Status;
                }
                await CompleteAsync(item.Id, chainRunId, resultStatus, ct);
            }
            finally { _tenantAccessor.Clear(); }

            if (item.ClientId is { } loggedClientId)
                _log.LogInformation(
                    "CRM automation '{Rule}' ran chain {ChainId} for client {ClientId}.",
                    item.RuleName, item.ChainId, loggedClientId);
            else
                _log.LogInformation(
                    "CRM ingestion automation '{Rule}' ran chain {ChainId}.",
                    item.RuleName, item.ChainId);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            await ReleaseOrFailAsync(item.Id, ex.Message, ct);
            _log.LogError(ex, "CRM automation '{Rule}' failed for client {ClientId}.",
                item.RuleName, item.ClientId);
        }
    }

    private async Task<TenantContext?> LoadTenantAsync(Guid id, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ITenantCatalog>().FindAsync(id, ct);
    }

    private async Task MarkRunningAsync(Guid id, Guid chainRunId, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var row = await db.CrmAutomationQueue.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return;
        row.ChainRunId = chainRunId;
        await db.SaveChangesAsync(ct);
    }

    private async Task CompleteAsync(
        Guid id, Guid chainRunId, string resultStatus, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var row = await db.CrmAutomationQueue.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return;
        row.ChainRunId = chainRunId;
        row.ResultStatus = resultStatus;
        row.CompletedAt = DateTimeOffset.UtcNow;
        row.InputPayloadProtected = null;
        await db.SaveChangesAsync(ct);
    }

    private async Task ReleaseOrFailAsync(Guid id, string error, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var encryptor = scope.ServiceProvider.GetRequiredService<IDataEncryptor>();
        var row = await db.CrmAutomationQueue.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return;
        row.Attempts++;
        var boundedError = error.Length > 1000 ? error[..1000] : error;
        row.LastError = encryptor.Protect(boundedError, DataEncryptionPurpose.CrmAutomation);
        row.ClaimedAt = null;
        row.ClaimedBy = null;
        if (row.Attempts >= MaxAttempts)
        {
            row.FailedAt = DateTimeOffset.UtcNow;
            row.ResultStatus = "Failed";
            row.InputPayloadProtected = null;
        }
        else row.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(Math.Pow(3, row.Attempts));
        await db.SaveChangesAsync(ct);
    }

    private async Task CleanupTrackingAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var cutoff = DateTimeOffset.UtcNow.Subtract(TrackingRetention);
        var removed = await db.CrmAutomationQueue
            .Where(row => row.CompletedAt < cutoff || row.FailedAt < cutoff)
            .ExecuteDeleteAsync(ct);
        _nextCleanupAt = DateTimeOffset.UtcNow.Add(CleanupInterval);
        if (removed > 0)
            _log.LogInformation("Removed {Count} expired CRM automation tracking receipts.", removed);
    }

    private static CrmAutomationQueueRow Clone(CrmAutomationQueueRow row) => new()
    {
        Id = row.Id,
        TenantId = row.TenantId,
        ProjectId = row.ProjectId,
        RuleId = row.RuleId,
        ClientId = row.ClientId,
        ChainId = row.ChainId,
        EventType = row.EventType,
        LifecycleStage = row.LifecycleStage,
        RuleName = row.RuleName,
        InputPayloadProtected = row.InputPayloadProtected,
        EnqueuedAt = row.EnqueuedAt,
        NextAttemptAt = row.NextAttemptAt,
        Attempts = row.Attempts,
        LastError = row.LastError,
        ClaimedBy = row.ClaimedBy,
        ClaimedAt = row.ClaimedAt,
        FailedAt = row.FailedAt,
        ChainRunId = row.ChainRunId,
        ResultStatus = row.ResultStatus,
        CompletedAt = row.CompletedAt,
    };
}
