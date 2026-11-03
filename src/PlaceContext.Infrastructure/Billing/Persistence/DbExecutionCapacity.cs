using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using PlaceContext.Application.Billing;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Billing;

/// <summary>Cluster-wide managed execution admission. A PostgreSQL plan row lock
/// serializes allocation across workers; heartbeated leases recover on process death.</summary>
public sealed class DbExecutionCapacity(
    AppDbContext db, ICurrentTenant tenant,
    IOptions<RunBillingDeploymentOptions> deployment,
    IOptions<PaymentBillingOptions> stripe,
    ILogger<DbExecutionCapacity> logger) : IExecutionCapacity
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PulseInterval = TimeSpan.FromSeconds(20);

    public async Task<IExecutionLease> AcquireAsync(Guid executionId, string kind, CancellationToken ct = default)
    {
        if (!tenant.IsResolved || tenant.TenantId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("A resolved tenant is required.");
        }
        if (executionId == Guid.Empty || kind is not ("workflow" or "job"))
        {
            throw new ArgumentException("An execution id and supported kind are required.");
        }
        var id = tenant.TenantId;
        var runKey = $"{kind}:{executionId:N}";
        // GetDbConnection().ConnectionString may redact Password once Npgsql has
        // opened the EF connection (Persist Security Info is false by default).
        // Use the originally configured EF connection string for new leases instead.
        // Never enable Persist Security Info or log this value.
        var cs = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("A PostgreSQL connection string is required.");
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            string? plan = null, status = null;
            DateTimeOffset starts = default, ends = default;
            await using (var select = conn.CreateCommand())
            {
                select.Transaction = tx;
                select.CommandText = """
                    SELECT "Plan", "Status", "PeriodStart", "PeriodEnd"
                    FROM tenant_run_plans WHERE "TenantId" = @tenant FOR UPDATE
                    """;
                select.Parameters.AddWithValue("tenant", id);
                await using var reader = await select.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    plan = reader.GetString(0);
                    status = reader.GetString(1);
                    starts = reader.GetFieldValue<DateTimeOffset>(2);
                    ends = reader.GetFieldValue<DateTimeOffset>(3);
                }
            }
            if (plan is null || plan == RunPlans.Developer && !deployment.Value.ManagedHosting)
            {
                if (plan is null && deployment.Value.ManagedHosting)
                {
                    throw new InvalidOperationException("An active managed subscription is required.");
                }
                await tx.CommitAsync(ct);
                return FreeLease.Instance;
            }
            var now = DateTimeOffset.UtcNow;
            if (plan == RunPlans.Developer && deployment.Value.ManagedHosting ||
                status is not ("active" or "trialing") || starts > now || ends <= now)
            {
                throw new InvalidOperationException("The subscription is inactive or expired.");
            }
            var limit = plan == RunPlans.Enterprise
                            ? stripe.Value.ContractFor(id)?.MaxConcurrentRuns
                                ?? throw new InvalidOperationException("Enterprise concurrency is not configured.")
                            : RunPlans.ConcurrentRuns(plan);
            if (limit <= 0)
            {
                throw new InvalidOperationException("A positive execution concurrency limit is required.");
            }
            await using (var cleanup = conn.CreateCommand())
            {
                cleanup.Transaction = tx;
                cleanup.CommandText = """
                    DELETE FROM tenant_execution_leases
                    WHERE "TenantId" = @tenant AND "ExpiresAt" < now()
                    """;
                cleanup.Parameters.AddWithValue("tenant", id);
                await cleanup.ExecuteNonQueryAsync(ct);
            }
            long count, sameRun;
            await using (var active = conn.CreateCommand())
            {
                active.Transaction = tx;
                active.CommandText = """
                    SELECT COUNT(*), COUNT(*) FILTER (WHERE "RunKey" = @run)
                    FROM tenant_execution_leases WHERE "TenantId" = @tenant
                    """;
                active.Parameters.AddWithValue("tenant", id);
                active.Parameters.AddWithValue("run", runKey);
                await using var reader = await active.ExecuteReaderAsync(ct);
                await reader.ReadAsync(ct);
                count = reader.GetInt64(0);
                sameRun = reader.GetInt64(1);
            }
            if (count < limit && sameRun == 0)
            {
                var token = Guid.NewGuid();
                await using var insert = conn.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO tenant_execution_leases
                        ("Token", "TenantId", "RunKey", "AcquiredAt", "ExpiresAt")
                    VALUES (@token, @tenant, @run, now(), now() + interval '2 minutes')
                    """;
                insert.Parameters.AddWithValue("token", token);
                insert.Parameters.AddWithValue("tenant", id);
                insert.Parameters.AddWithValue("run", runKey);
                await insert.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);
                return new LiveLease(cs, id, token, logger);
            }
            await tx.CommitAsync(ct);
            await Task.Delay(PollInterval, ct);
        }
    }

    private sealed class FreeLease : IExecutionLease
    {
        public static readonly FreeLease Instance = new();
        public CancellationToken LostToken => CancellationToken.None;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class LiveLease : IExecutionLease
    {
        private readonly string _cs;
        private readonly Guid _tenant, _token;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _stop = new();
        private readonly CancellationTokenSource _lost = new();
        private readonly Task _heartbeat;
        public CancellationToken LostToken => _lost.Token;

        public LiveLease(string cs, Guid tenant, Guid token, ILogger logger)
        {
            (_cs, _tenant, _token, _logger) = (cs, tenant, token, logger);
            _heartbeat = HeartbeatAsync();
        }

        private async Task HeartbeatAsync()
        {
            using var timer = new PeriodicTimer(PulseInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(_stop.Token))
                {
                    await using var conn = new NpgsqlConnection(_cs);
                    await conn.OpenAsync(_stop.Token);
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = """
                        UPDATE tenant_execution_leases
                        SET "ExpiresAt" = now() + interval '2 minutes'
                        WHERE "Token" = @token AND "TenantId" = @tenant AND "ExpiresAt" >= now()
                        """;
                    cmd.Parameters.AddWithValue("tenant", _tenant);
                    cmd.Parameters.AddWithValue("token", _token);
                    if (await cmd.ExecuteNonQueryAsync(_stop.Token) != 1)
                    {
                        _logger.LogError("Execution capacity lease was lost for {Tenant}.", _tenant);
                        _lost.Cancel();
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Execution capacity heartbeat failed for {Tenant}.", _tenant);
                _lost.Cancel();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            await _heartbeat;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await using var conn = new NpgsqlConnection(_cs);
                await conn.OpenAsync(timeout.Token);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    DELETE FROM tenant_execution_leases
                    WHERE "Token" = @token AND "TenantId" = @tenant
                    """;
                cmd.Parameters.AddWithValue("tenant", _tenant);
                cmd.Parameters.AddWithValue("token", _token);
                await cmd.ExecuteNonQueryAsync(timeout.Token);
            }
            catch (Exception ex)
            {
                // TTL is the crash recovery fallback.
                _logger.LogWarning(ex, "Could not release the execution capacity lease for {Tenant}.", _tenant);
            }
            _stop.Dispose();
        }
    }
}
