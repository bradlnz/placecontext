using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Infrastructure.Scheduling;

/// <summary>Durably drains lifecycle-event automation rules without blocking the CRM request.</summary>
public sealed class CrmAutomationWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private const int BatchSize = 4;
    private const int MaxAttempts = 3;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CrmAutomationWorker> _log;
    private readonly string _instanceId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public CrmAutomationWorker(
        IServiceScopeFactory scopes,
        ILogger<CrmAutomationWorker> log)
        => (_scopes, _log) = (scopes, log);

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
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _log.LogError(ex, "CRM automation queue drain failed."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<IReadOnlyList<CrmAutomationQueueRow>> ClaimAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var rows = await db.CrmAutomationQueue.FromSqlRaw(
                """
                SELECT * FROM crm_automation_queue
                WHERE "FailedAt" IS NULL
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
            CurrentTenant.Set(tenant);
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<
                    ICommandHandler<RunCrmClientAutomationCommand, CrmChainRunView>>();
                await handler.HandleAsync(
                    new RunCrmClientAutomationCommand(item.ClientId, item.ChainId), ct);
            }
            finally { CurrentTenant.Clear(); }

            await DeleteAsync(item.Id, ct);
            _log.LogInformation(
                "CRM automation '{Rule}' ran chain {ChainId} for client {ClientId}.",
                item.RuleName, item.ChainId, item.ClientId);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            await ReleaseOrFailAsync(item.Id, ex.Message, ct);
            _log.LogError(ex, "CRM automation '{Rule}' failed for client {ClientId}.",
                item.RuleName, item.ClientId);
        }
    }

    private async Task<TenantInfo?> LoadTenantAsync(Guid id, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : new TenantInfo(row.Id, row.Slug, row.Name, row.TimeZoneId);
    }

    private async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.CrmAutomationQueue.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
    }

    private async Task ReleaseOrFailAsync(Guid id, string error, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.CrmAutomationQueue.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return;
        row.Attempts++;
        row.LastError = error.Length > 1000 ? error[..1000] : error;
        row.ClaimedAt = null;
        row.ClaimedBy = null;
        if (row.Attempts >= MaxAttempts) row.FailedAt = DateTimeOffset.UtcNow;
        else row.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(Math.Pow(3, row.Attempts));
        await db.SaveChangesAsync(ct);
    }

    private static CrmAutomationQueueRow Clone(CrmAutomationQueueRow row) => new()
    {
        Id = row.Id,
        TenantId = row.TenantId,
        RuleId = row.RuleId,
        ClientId = row.ClientId,
        ChainId = row.ChainId,
        EventType = row.EventType,
        LifecycleStage = row.LifecycleStage,
        RuleName = row.RuleName,
        EnqueuedAt = row.EnqueuedAt,
        NextAttemptAt = row.NextAttemptAt,
        Attempts = row.Attempts,
        LastError = row.LastError,
        ClaimedBy = row.ClaimedBy,
        ClaimedAt = row.ClaimedAt,
        FailedAt = row.FailedAt,
    };
}
