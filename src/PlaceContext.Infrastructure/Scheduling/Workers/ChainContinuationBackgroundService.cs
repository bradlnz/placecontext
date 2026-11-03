using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Secrets;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Infrastructure.Scheduling;

/// <summary>Claims due wait gates and resumes the same persisted chain run on any replica.</summary>
public sealed class ChainContinuationBackgroundService : BackgroundService
{
    // Slightly slower polling reduces deploy-time CPU contention while still keeping
    // waits, resumes, and chain-continuation behavior responsive.
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ChainContinuationBackgroundService> _log;
    private readonly string _instanceId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public ChainContinuationBackgroundService(IServiceScopeFactory scopes, ILogger<ChainContinuationBackgroundService> log)
        => (_scopes, _log) = (scopes, log);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                foreach (var continuation in await ClaimAsync(stoppingToken))
                {
                    await ResumeAsync(continuation, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Chain continuation drain failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<IReadOnlyList<Continuation>> ClaimAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rows = await db.ChainRuns.FromSqlInterpolated(
                $"""
                WITH due AS (SELECT "Id" FROM chain_runs
                WHERE "Status" = 'Waiting'
                  AND "ResumeAt" <= now()
                  AND ("ContinuationClaimedAt" IS NULL
                       OR "ContinuationClaimedAt" < now() - interval '5 minutes')
                ORDER BY "ResumeAt"
                FOR UPDATE SKIP LOCKED
                LIMIT 4)
                UPDATE chain_runs AS run
                SET "ContinuationClaimedAt" = now(), "ContinuationClaimedBy" = {_instanceId}
                FROM due WHERE run."Id" = due."Id"
                RETURNING run.*
                """).IgnoreQueryFilters().AsNoTracking()
            .ToListAsync(ct);
        await tx.CommitAsync(ct);
        return rows.Select(row => new Continuation(
            row.Id, row.TenantId, row.ChainId, row.ResumeStageIndex!.Value)).ToList();
    }

    private async Task ResumeAsync(Continuation value, CancellationToken ct)
    {
        try
        {
            var tenant = await LoadTenantAsync(value.TenantId, ct)
                ?? throw new InvalidOperationException($"Tenant {value.TenantId} no longer exists.");
            CurrentTenant.Set(tenant);
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var persisted = await db.ChainRuns.AsNoTracking()
                    .FirstAsync(row => row.Id == value.RunId, ct);
                var encryptor = scope.ServiceProvider.GetRequiredService<PlaceContext.Application.Secrets.IDataEncryptor>();
                if (persisted.Status != "Waiting")
                {
                    return;
                }
                if (persisted.InputRequest is { } protectedRequest)
                {
                    var inputRequest = System.Text.Json.JsonSerializer.Deserialize<ChainInputRequest>(
                        encryptor.Unprotect(protectedRequest, PlaceContext.Application.Secrets.IDataEncryptor.Purpose.ChainRun));
                    if (inputRequest?.SubmittedBy is { } userId)
                    {
                        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Id == userId, ct)
                            ?? throw new InvalidOperationException("The input submitter no longer exists.");
                        CurrentUser.Set(new UserIdentity(user.Id, user.Role));
                    }
                }
                var payload = persisted.FinalOutput is null ? null : encryptor.Unprotect(
                    persisted.FinalOutput, PlaceContext.Application.Secrets.IDataEncryptor.Purpose.ChainRun);
                IReadOnlyDictionary<int, string>? overrides = null;
                if (persisted.ContinuationOverrides is { } protectedOverrides)
                {
                    var json = encryptor.Unprotect(
                        protectedOverrides, PlaceContext.Application.Secrets.IDataEncryptor.Purpose.ChainRun);
                    overrides = System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, string>>(json);
                }
                var handler = scope.ServiceProvider.GetRequiredService<
                    ICommandHandler<RunJobChainCommand, ChainRunView>>();
                await handler.HandleAsync(new RunJobChainCommand(
                    value.ChainId, payload, value.RunId, overrides,
                    ResumeFromStageIndex: value.StageIndex), ct);
            }
            finally
            {
                CurrentUser.Clear();
                CurrentTenant.Clear();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await ReleaseAsync(value.RunId, ct);
            _log.LogError(ex, "Could not resume chain run {ChainRunId}.", value.RunId);
        }
    }

    private async Task<TenantInfo?> LoadTenantAsync(Guid id, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : new TenantInfo(row.Id, row.Slug, row.Name, row.TimeZoneId);
    }

    private async Task ReleaseAsync(Guid runId, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantId = await db.ChainRuns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Id == runId).Select(x => (Guid?)x.TenantId).SingleOrDefaultAsync(ct);
        if (tenantId is null)
        {
            return;
        }
        await using var tenantDb = new AppDbContext(db.ContextOptions, new TenantInfo(tenantId.Value, "", "", "UTC"));
        var row = await tenantDb.ChainRuns.FirstOrDefaultAsync(x => x.Id == runId, ct);
        // Dispatch can fail after Resume has marked the run Running. Keep its checkpoint
        // retryable, without reviving cancellation, completion, or a new input request.
        if (row is null || row.Status is not ("Waiting" or "Running"))
        {
            return;
        }
        row.Status = "Waiting";
        row.ContinuationClaimedAt = null;
        row.ContinuationClaimedBy = null;
        await tenantDb.SaveChangesAsync(ct);
    }

    private sealed record Continuation(Guid RunId, Guid TenantId, Guid ChainId, int StageIndex);
}
