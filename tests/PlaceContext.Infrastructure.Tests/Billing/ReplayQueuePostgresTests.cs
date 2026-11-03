using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlaceContext.Application.Access;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Scheduling;
using PlaceContext.Infrastructure.Tenancy;
using PlaceContext.Infrastructure.Tests.AgentWorkflows;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests.Billing;

public sealed class ReplayQueuePostgresTests
{
    [AgentPostgresFact]
    public async Task Replay_metadata_is_durable_and_tenant_scoped()
    {
        var schema = "replayq_" + Guid.NewGuid().ToString("N");
        var cs = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES"))
        {
            SearchPath = schema
        }.ConnectionString;
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using (var setup = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", conn))
        {
            await setup.ExecuteNonQueryAsync();
        }
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var project = Guid.NewGuid();
        var chain = Guid.NewGuid();
        var original = Guid.NewGuid();
        try
        {
            await using (var schemaCommand = new NpgsqlCommand("""
                CREATE TABLE job_chains ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL);
                CREATE TABLE chain_runs ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL,
                    "ProjectId" uuid NOT NULL, "ChainId" uuid NOT NULL, "Status" text NOT NULL);
                """, conn))
            {
                await schemaCommand.ExecuteNonQueryAsync();
            }
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;
            await using var db = new AppDbContext(options, tenant);
            typeof(PlaceContext.Infrastructure.DependencyInjection)
                .GetMethod("EnsureRpcChainSubmissionSchema", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { db });
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO job_chains VALUES ({chain}, {tenant.TenantId}, {project})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO chain_runs VALUES ({original}, {tenant.TenantId}, {project}, {chain}, {"Failed"})");
            CurrentUser.Set(new UserIdentity(Guid.NewGuid(), "Admin"));
            var queue = new DbJobChainSubmissionQueue(db, tenant, new CurrentUser(),
                new FakeDataEncryptor(), new Allowed());
            var first = await queue.EnqueueAsync(project, chain, "{\"retry\":true}",
                "replay:one", replayOriginalRunId: original, replayFromStepIndex: 1);
            var duplicate = await queue.EnqueueAsync(project, chain, "{\"retry\":true}",
                "replay:one", replayOriginalRunId: original, replayFromStepIndex: 1);
            Assert.Equal(first.TrackingId, duplicate.TrackingId);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                queue.EnqueueAsync(project, chain, "{\"retry\":true}", "replay:one",
                    replayOriginalRunId: original, replayFromStepIndex: 2));
            await using var lookup = new NpgsqlCommand(
                "SELECT \"ReplayOfRunId\", \"ReplayFromStepIndex\" FROM rpc_chain_submissions WHERE \"Id\" = @id", conn);
            lookup.Parameters.AddWithValue("id", first.TrackingId);
            await using var reader = await lookup.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(original, reader.GetGuid(0));
            Assert.Equal(1, reader.GetInt32(1));
        }
        finally
        {
            CurrentUser.Clear();
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", conn);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private sealed class Allowed : IPermissionService
    {
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(
            Guid id, string role, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
