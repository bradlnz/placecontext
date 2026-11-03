using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PlaceContext.Application.Access;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Secrets;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Scheduling;
using PlaceContext.Infrastructure.Tenancy;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class ChainQueueTests
{
    [ChainInputResumeTests.PostgresFact]
    public async Task Queue_preserves_payload_scopes_receipts_and_distributes_claims_without_duplicates()
    {
        var schema = "queue_test_" + Guid.NewGuid().ToString("N");
        var cs = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES"))
        {
            SearchPath = schema
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await using (var setup = new NpgsqlCommand($"""
            CREATE SCHEMA "{schema}";
            CREATE TABLE job_chains ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL);
            CREATE TABLE chain_runs ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "Status" text NOT NULL, "FinishedAt" timestamptz);
            """, connection))
        {
            await setup.ExecuteNonQueryAsync();
        }
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;
        var project = Guid.NewGuid();
        var chain = Guid.NewGuid();
        CurrentUser.Set(new UserIdentity(Guid.NewGuid(), "Admin"));
        try
        {
            await using var db = new AppDbContext(options, tenant);
            typeof(PlaceContext.Infrastructure.DependencyInjection).GetMethod("EnsureRpcChainSubmissionSchema", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { db });
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO job_chains VALUES ({chain}, {tenant.TenantId}, {project})");
            var encryptor = new FakeDataEncryptor();
            var permissions = new Permissions();
            DbChainQueueService Service(AppDbContext context, FakeCurrentTenant owner) => new(context, owner, permissions,
                new DbJobChainSubmissionQueue(context, owner, new CurrentUser(), encryptor, permissions), encryptor);
            var queues = Service(db, tenant);
            var submit = new DbJobChainSubmissionQueue(
                db, tenant, new CurrentUser(), encryptor, permissions);
            var stepValues = new Dictionary<int, string>
            {
                [0] = "{\"account\":\"Ada\"}",
                [2] = "{\"approved\":true}"
            };
            var portalRun = await submit.EnqueueAsync(project, chain, null,
                "portal:test-durable-overrides", stepPayloadOverrides: stepValues);
            var repeated = await submit.EnqueueAsync(project, chain, null,
                "portal:test-durable-overrides", stepPayloadOverrides: stepValues);
            Assert.Equal(portalRun.TrackingId, repeated.TrackingId);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                submit.EnqueueAsync(project, chain, "{\"different\":true}",
                    "portal:test-durable-overrides", stepPayloadOverrides: stepValues));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                submit.EnqueueAsync(project, chain, null, "portal:test-durable-overrides",
                    stepPayloadOverrides: new Dictionary<int, string> { [0] = "{}" }));
            await using (var sql = new NpgsqlCommand(
                "SELECT \"StepPayloadOverrides\" FROM rpc_chain_submissions WHERE \"Id\" = @id", connection))
            {
                sql.Parameters.AddWithValue("id", portalRun.TrackingId);
                var persisted = (string?)await sql.ExecuteScalarAsync();
                Assert.NotNull(persisted);
                var recovered = JsonSerializer.Deserialize<Dictionary<int, string>>(
                    encryptor.Unprotect(persisted!, IDataEncryptor.Purpose.RpcChainSubmission));
                Assert.Equal(stepValues, recovered);
            }
            await Assert.ThrowsAsync<ArgumentException>(() =>
                submit.EnqueueAsync(Guid.NewGuid(), chain, null));
            // Scrub completed payloads; retries must still validate the saved digest.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE rpc_chain_submissions SET \"Status\" = 'Succeeded', \"FinishedAt\" = now(), \"InputPayload\" = NULL, \"StepPayloadOverrides\" = NULL WHERE \"Id\" = {portalRun.TrackingId}");
            var afterCompletion = await submit.EnqueueAsync(project, chain, null,
                "portal:test-durable-overrides", stepPayloadOverrides: stepValues);
            Assert.Equal(portalRun.TrackingId, afterCompletion.TrackingId);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                submit.EnqueueAsync(project, chain, null, "portal:test-durable-overrides",
                    stepPayloadOverrides: new Dictionary<int, string> { [0] = "{}" }));
            var queue = await queues.SaveAsync(null, project, chain, "incoming", true);
            var second = await queues.SaveAsync(null, project, chain, "other", true);
            await Assert.ThrowsAsync<ArgumentException>(() => queues.SaveAsync(null, project, chain, "invalid TTL", true, 0));
            await Assert.ThrowsAsync<ArgumentException>(() => queues.SaveAsync(null, project, chain, "invalid retention", true, 60, 366));
            await Assert.ThrowsAsync<ArgumentException>(() => queues.SaveAsync(null, Guid.NewGuid(), chain, "wrong project", true));
            await Assert.ThrowsAsync<ArgumentException>(() => queues.SubmitAsync(queue.Id, JsonSerializer.SerializeToElement(new[] { 1 }), null));
            var payload = JsonSerializer.SerializeToElement(new
            {
                orderId = "123",
                customer = new
                {
                    name = "Ada"
                },
                items = new[] { new { quantity = 2 } }
            });
            var receipt = await queues.SubmitAsync(queue.Id, payload, "same-message");
            Assert.Equal(receipt.TrackingId, (await queues.SubmitAsync(queue.Id, payload, "same-message")).TrackingId);
            Assert.NotEqual(receipt.TrackingId, (await queues.SubmitAsync(second.Id, payload, "same-message")).TrackingId);
            Assert.Null(await queues.GetMessageAsync(second.Id, receipt.TrackingId));
            await using (var read = new NpgsqlCommand("SELECT \"InputPayload\" FROM rpc_chain_submissions WHERE \"Id\" = @id", connection))
            {
                read.Parameters.AddWithValue("id", receipt.TrackingId);
                Assert.Equal(payload.GetRawText(), await read.ExecuteScalarAsync());
            }
            var otherTenant = new FakeCurrentTenant(Guid.NewGuid());
            await using (var otherDb = new AppDbContext(options, otherTenant))
            {
                var other = Service(otherDb, otherTenant);
                Assert.Empty(await other.ListAsync(project));
                Assert.Null(await other.GetMessageAsync(queue.Id, receipt.TrackingId));
                await Assert.ThrowsAsync<KeyNotFoundException>(() => other.DeleteAsync(queue.Id));
                await Assert.ThrowsAsync<KeyNotFoundException>(() => other.SubmitAsync(queue.Id, payload, null));
                await Assert.ThrowsAsync<ArgumentException>(() => other.SaveAsync(null, project, chain, "cross tenant", true));
            }
            await queues.SaveAsync(queue.Id, project, chain, "incoming", false);
            await Assert.ThrowsAsync<InvalidOperationException>(() => queues.SubmitAsync(queue.Id, payload, null));
            await queues.SaveAsync(queue.Id, project, chain, "incoming", true);
            for (var i = 0; i < 6; i++)
            {
                await queues.SubmitAsync(queue.Id, payload, null);
            }
            permissions.Allowed = false;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => queues.DeleteAsync(queue.Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => queues.SubmitAsync(queue.Id, payload, null));
            permissions.Allowed = true;
            await queues.DeleteAsync(queue.Id);
            Assert.DoesNotContain(await queues.ListAsync(project), item => item.Id == queue.Id);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => queues.SubmitAsync(queue.Id, payload, null));
            Assert.NotNull(await queues.GetMessageAsync(queue.Id, receipt.TrackingId));
            await using var provider = new ServiceCollection().AddScoped(_ => new AppDbContext(options, tenant)).BuildServiceProvider();
            using var firstWorker = new ProbeWorker(provider.GetRequiredService<IServiceScopeFactory>());
            using var secondWorker = new ProbeWorker(provider.GetRequiredService<IServiceScopeFactory>());
            var claimed = await Task.WhenAll(firstWorker.Claim(), secondWorker.Claim());
            Assert.All(claimed, batch => Assert.Equal(4, batch.Length));
            Assert.Equal(8, claimed.SelectMany(batch => batch).Distinct().Count());
            Assert.Empty(await firstWorker.Claim());
            second = await queues.SaveAsync(second.Id, project, chain, "other", true, 60, 1);
            Assert.Equal(60, second.MessageTtlSeconds);
            Assert.Equal(1, second.ReceiptRetentionDays);
            var expired = await queues.SubmitAsync(second.Id, payload, null);
            var stale = await queues.SubmitAsync(second.Id, payload, null);
            var established = await queues.SubmitAsync(second.Id, payload, null);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE rpc_chain_submissions SET \"ExpiresAt\" = now() - interval '1 second' WHERE \"Id\" IN ({expired.TrackingId}, {stale.TrackingId}, {established.TrackingId})");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE rpc_chain_submissions SET \"Status\" = 'Running', \"HeartbeatAt\" = now() - interval '3 minutes' WHERE \"Id\" = {stale.TrackingId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO chain_runs VALUES ({established.ChainRunId}, {tenant.TenantId}, 'AwaitingInput', NULL)");
            Assert.Equal(established.TrackingId, Assert.Single(await firstWorker.Claim()));
            Assert.Equal("Expired", (await queues.GetMessageAsync(second.Id, expired.TrackingId))!.Status);
            Assert.Equal("Expired", (await queues.GetMessageAsync(second.Id, stale.TrackingId))!.Status);
            Assert.Equal("AwaitingInput", (await queues.GetMessageAsync(second.Id, established.TrackingId))!.Status);
            await using (var read = new NpgsqlCommand("SELECT \"InputPayload\" FROM rpc_chain_submissions WHERE \"Id\" = @id", connection))
            {
                read.Parameters.AddWithValue("id", expired.TrackingId);
                Assert.Equal(DBNull.Value, await read.ExecuteScalarAsync());
            }
            // Accepted messages retain their settings even when configuration changes or is removed.
            await queues.SaveAsync(second.Id, project, chain, "other", true, 3600, 30);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE rpc_chain_submissions SET \"FinishedAt\" = now() - interval '2 days' WHERE \"Id\" IN ({receipt.TrackingId}, {expired.TrackingId})");
            await firstWorker.Cleanup();
            Assert.Null(await queues.GetMessageAsync(second.Id, expired.TrackingId));
            Assert.NotNull(await queues.GetMessageAsync(queue.Id, receipt.TrackingId));
            Assert.NotNull(await queues.GetMessageAsync(second.Id, established.TrackingId));
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO chain_runs VALUES ({receipt.ChainRunId}, {tenant.TenantId}, 'Succeeded', now())");
            Assert.Equal("Succeeded", (await queues.GetMessageAsync(queue.Id, receipt.TrackingId))!.Status);
            await Assert.ThrowsAsync<ArgumentException>(() => queues.SaveAsync(null, project, chain, "bad retry", true, 60, 1, 0));
            await queues.SaveAsync(second.Id, project, chain, "retrying", true, 3600, 1, 2, 60);
            var retry = await queues.SubmitAsync(second.Id, payload, "retry-me");
            Assert.Equal(retry.TrackingId, Assert.Single(await firstWorker.Claim()));
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO chain_runs VALUES ({retry.ChainRunId}, {tenant.TenantId}, 'Failed', now())");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE rpc_chain_submissions SET \"Status\" = 'AwaitingInput' WHERE \"Id\" = {retry.TrackingId}");
            Assert.Equal("RetryPending", (await queues.GetMessageAsync(second.Id, retry.TrackingId))!.Status);
            var reconcilers = await Task.WhenAll(firstWorker.Claim(), secondWorker.Claim());
            Assert.All(reconcilers, Assert.Empty);
            var next = (await queues.GetMessageAsync(second.Id, retry.TrackingId))!;
            Assert.Equal("Queued", next.Status);
            Assert.Equal(1, next.Attempts);
            Assert.NotEqual(retry.ChainRunId, next.ChainRunId);
            Assert.Contains(retry.ChainRunId, next.PreviousRunIds);
            // A delayed callback from the previous attempt cannot overwrite the new one.
            await firstWorker.Reflect(retry.TrackingId, new(retry.ChainRunId, chain, "retry", "Failed", [], null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            Assert.Equal("Queued", (await queues.GetMessageAsync(second.Id, retry.TrackingId))!.Status);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE rpc_chain_submissions SET \"NextAttemptAt\" = now() WHERE \"Id\" = {retry.TrackingId}");
            Assert.Equal(retry.TrackingId, Assert.Single(await firstWorker.Claim()));
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO chain_runs VALUES ({next.ChainRunId}, {tenant.TenantId}, 'Failed', now())");
            Assert.Empty(await firstWorker.Claim());
            var exhausted = (await queues.GetMessageAsync(second.Id, retry.TrackingId))!;
            Assert.Equal("Failed", exhausted.Status);
            Assert.Equal(2, exhausted.Attempts);
            Assert.Equal(2, exhausted.PreviousRunIds.Count);
            await queues.RetryAsync(second.Id, retry.TrackingId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => queues.RetryAsync(second.Id, retry.TrackingId));
            var manual = (await queues.GetMessageAsync(second.Id, retry.TrackingId))!;
            Assert.Equal("Queued", manual.Status);
            Assert.Equal(0, manual.Attempts);
            Assert.NotEqual(exhausted.ChainRunId, manual.ChainRunId);
            Assert.Equal(retry.TrackingId, Assert.Single(await firstWorker.Claim()));
            await firstWorker.Reflect(retry.TrackingId, new(manual.ChainRunId, chain, "retry", "Succeeded", [], null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            Assert.Equal("Succeeded", (await queues.GetMessageAsync(second.Id, retry.TrackingId))!.Status);
            await queues.SaveAsync(second.Id, project, chain, "delayed", true, 60, 1, 2, 3, 600);
            var delayed = await queues.SubmitAsync(second.Id, payload, "delay");
            Assert.True(delayed.AvailableAt > DateTimeOffset.UtcNow.AddMinutes(9));
            Assert.Equal(TimeSpan.FromSeconds(60), delayed.ExpiresAt - delayed.AvailableAt);
            var scheduledAt = DateTimeOffset.UtcNow.AddDays(1);
            var scheduled = await queues.SubmitAsync(second.Id, payload, "scheduled", scheduledAt);
            Assert.True((scheduled.AvailableAt!.Value - scheduledAt).Duration() < TimeSpan.FromMilliseconds(1));
            Assert.Empty(await firstWorker.Claim());
            await Assert.ThrowsAsync<ArgumentException>(() => queues.SubmitAsync(second.Id, payload, null, DateTimeOffset.UtcNow.AddMinutes(-1)));
            await Assert.ThrowsAsync<ArgumentException>(() => queues.SubmitAsync(second.Id, payload, null, DateTimeOffset.UtcNow.AddDays(366)));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE rpc_chain_submissions SET \"NextAttemptAt\" = now() WHERE \"Id\" = {delayed.TrackingId}");
            Assert.Equal(delayed.TrackingId, Assert.Single(await firstWorker.Claim()));
        }
        finally
        {
            CurrentUser.Clear();
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private sealed class ProbeWorker(IServiceScopeFactory scopes) : RpcChainSubmissionBackgroundService(scopes,
        NullLogger<RpcChainSubmissionBackgroundService>.Instance, new ConfigurationBuilder().Build())
    {
        public Task Reflect(Guid id, ChainRunView run) => (Task)typeof(RpcChainSubmissionBackgroundService)
            .GetMethod("ReflectRunStatusAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(this, new object[] { id, run, CancellationToken.None })!;
        public Task Cleanup() => CleanupTrackingAsync(CancellationToken.None);
        public async Task<Guid[]> Claim() => (await ClaimAsync(4, CancellationToken.None)).Select(row => row.Id).ToArray();
    }
    private sealed class Permissions : IPermissionService
    {
        public bool Allowed { get; set; } = true;
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(Allowed);
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid userId, string roleName, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
