using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tests.AgentWorkflows;
using PlaceContext.Infrastructure.Scheduling;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests.Billing;

public sealed class ManagedJobSubmissionPostgresTests
{
    [AgentPostgresFact]
    public async Task Managed_jobs_are_durable_idempotent_and_tenant_isolated()
    {
        var schema = "managed_job_" + Guid.NewGuid().ToString("N");
        var cs = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES")) { SearchPath = schema }.ConnectionString;
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using (var setup = new NpgsqlCommand($"""
            CREATE SCHEMA "{schema}";
            CREATE TABLE jobs ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL);
            CREATE TABLE job_runs ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "Status" text NOT NULL);
            """, conn))
            await setup.ExecuteNonQueryAsync();
        try
        {
            var tenant = new FakeCurrentTenant(Guid.NewGuid());
            var projectId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            var otherJobId = Guid.NewGuid();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;
            await using var db = new AppDbContext(options, tenant);
            typeof(PlaceContext.Infrastructure.DependencyInjection)
                .GetMethod("EnsureRpcJobSubmissionSchema", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { db });
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO jobs VALUES ({jobId}, {tenant.TenantId}, {projectId})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO jobs VALUES ({otherJobId}, {tenant.TenantId}, {projectId})");
            var encryptor = new FakeDataEncryptor();
            var queue = new DbJobRunSubmissionQueue(db, tenant, encryptor);
            var runId = Guid.NewGuid();
            var receipt = await queue.EnqueueAsync(projectId, jobId, "{\"input\":42}", runId);
            Assert.Equal("Queued", receipt.Status);
            Assert.Equal(runId, receipt.RunId);
            var duplicate = await queue.EnqueueAsync(projectId, jobId, "{\"input\":42}", runId);
            Assert.Equal(receipt.TrackingId, duplicate.TrackingId);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                queue.EnqueueAsync(projectId, jobId, "{\"input\":43}", runId));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                queue.EnqueueAsync(projectId, otherJobId, "{\"input\":42}", runId));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                queue.EnqueueAsync(Guid.NewGuid(), jobId, null));
            Assert.NotNull(await queue.GetAsync(projectId, jobId, receipt.TrackingId));
            Assert.Null(await queue.GetAsync(projectId, otherJobId, receipt.TrackingId));
            Assert.Null(await queue.GetAsync(Guid.NewGuid(), jobId, receipt.TrackingId));
            await using (var otherDb = new AppDbContext(options, new FakeCurrentTenant(Guid.NewGuid())))
            {
                Assert.Null(await new DbJobRunSubmissionQueue(otherDb,
                    new FakeCurrentTenant(Guid.NewGuid()), encryptor)
                    .GetAsync(projectId, jobId, receipt.TrackingId));
            }
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO job_runs VALUES ({runId}, {tenant.TenantId}, {"Succeeded"})");
            Assert.Equal("Succeeded", (await queue.GetAsync(projectId, jobId, receipt.TrackingId))!.Status);
            Assert.Equal(1, await RpcJobSubmissionWorker.ReconcileFinishedAsync(db));
            Assert.Equal("Succeeded", await db.Database
                .SqlQueryRaw<string>("SELECT \"Status\" AS \"Value\" FROM rpc_job_submissions")
                .SingleAsync());
            Assert.Equal(0, await RpcJobSubmissionWorker.ReconcileFinishedAsync(db));
            Assert.Equal(1L, await db.Database
                .SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM rpc_job_submissions")
                .SingleAsync());
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", conn);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
