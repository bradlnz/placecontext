using PlaceContext.Application.Features;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace PlaceContext.Infrastructure.Scheduling;

/// <summary>
/// Drives the per-project agent (<see cref="ProjectAgentService"/>): on an interval, one replica
/// (Postgres advisory lock — same leader-election trick as <see cref="TriggerSchedulerService"/>)
/// walks every tenant's projects and lets each project's agent figure out what to do next. Interval
/// via <c>PlaceContext:Agent:IntervalMinutes</c> (default 60); disable with
/// <c>PlaceContext:Agent:Enabled = false</c>.
/// </summary>
public sealed class ProjectAgentSchedulerService : BackgroundService
{
    private const long AgentLockKey = 0x504C4143_4378_6167L; // "PLAC...ag"

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ProjectAgentSchedulerService> _log;
    private readonly bool _enabled;
    private readonly TimeSpan _interval;

    public ProjectAgentSchedulerService(IServiceScopeFactory scopes, IConfiguration config,
        ILogger<ProjectAgentSchedulerService> log)
    {
        _scopes = scopes;
        _log = log;
        var section = config.GetSection("PlaceContext:Agent");
        _enabled = section.GetValue("Enabled", true);
        _interval = TimeSpan.FromMinutes(Math.Clamp(section.GetValue("IntervalMinutes", 60), 5, 24 * 60));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _log.LogInformation("Project agent is disabled (PlaceContext:Agent:Enabled = false).");
            return;
        }
        using var timer = new PeriodicTimer(_interval);
        do
        {
            try { await RunPassAsLeaderAsync(stoppingToken); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _log.LogError(ex, "Project agent pass failed; will retry next interval."); }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private async Task RunPassAsLeaderAsync(CancellationToken ct)
    {
        await using var lockScope = _scopes.CreateAsyncScope();
        var db = lockScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);

        if (await ExecScalarAsync(conn, "SELECT pg_try_advisory_lock(@key)", ct) is not true)
            return; // another replica is running the agents this tick
        try
        {
            foreach (var tenant in await LoadTenantsAsync(ct))
            {
                CurrentTenant.Set(tenant);
                try
                {
                    await using var scope = _scopes.CreateAsyncScope();
                    var projects = await scope.ServiceProvider.GetRequiredService<IProjectRepository>().ListAsync(ct);
                    foreach (var project in projects)
                    {
                        ct.ThrowIfCancellationRequested();
                        // A fresh scope per project keeps one project's failure from poisoning the rest.
                        await using var agentScope = _scopes.CreateAsyncScope();
                        var agent = agentScope.ServiceProvider.GetRequiredService<ProjectAgentService>();
                        var result = await agent.RunAsync(project.Id.Value, ct);
                        if (result is { QueuedTitles.Count: > 0 })
                            _log.LogInformation("Agent for {Project} ({Tenant}) queued: {Titles}",
                                project.Name, tenant.Slug, string.Join(" · ", result.QueuedTitles));
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Project agent failed for tenant {Slug}.", tenant.Slug);
                }
                finally { CurrentTenant.Clear(); }
            }
        }
        finally
        {
            await ExecScalarAsync(conn, "SELECT pg_advisory_unlock(@key)", ct);
        }
    }

    private async Task<IReadOnlyList<TenantInfo>> LoadTenantsAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tenants.AsNoTracking()
            .Select(t => new TenantInfo(t.Id, t.Slug, t.Name, t.TimeZoneId)).ToListAsync(ct);
    }

    private static async Task<object?> ExecScalarAsync(NpgsqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("key", AgentLockKey);
        return await cmd.ExecuteScalarAsync(ct);
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
