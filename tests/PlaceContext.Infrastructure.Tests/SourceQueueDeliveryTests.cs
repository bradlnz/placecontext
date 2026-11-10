using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlaceContext.Application.DataSources;
using PlaceContext.Application.Search;
using PlaceContext.Application.Secrets;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.DataSources;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Scheduling;
using PlaceContext.Infrastructure.Security;
using PlaceContext.Infrastructure.Tests.AgentWorkflows;
using PlaceContext.TestSupport;
using PlaceContext.Application.Access;

namespace PlaceContext.Infrastructure.Tests;

public sealed class SourceQueueDeliveryTests
{
    [Theory]
    [InlineData(10, 0, 26)]
    [InlineData(250, 0, 2)]
    [InlineData(250, 15000, 84)]
    public void Queue_batches_preserve_all_records_and_fit_review_context(int size, int textLength, int expectedBatches)
    {
        var source = new DataSourceDefinition(Guid.NewGuid(), Guid.NewGuid(), "Orders", "public", "orders", "id",
            ["id", "body"], [], false, Guid.NewGuid(), Queue: new(Guid.NewGuid(), [], [], BatchSize: size));
        var rows = Enumerable.Range(1, 251).Select(id => new SourceRecord(id.ToString(),
            JsonSerializer.Serialize(new { id, body = new string('x', textLength) }))).ToArray();
        var documents = SourcePreparation.Prepare(source, rows, DateTimeOffset.UtcNow);
        var payloads = SourceQueueDelivery.RecordPayloads(source, documents).ToArray();
        Assert.Equal(expectedBatches, payloads.Length);
        Assert.Equal(251, payloads.Sum(payload => payload.GetProperty("records").GetArrayLength()));
        Assert.Equal(Enumerable.Range(1, 251), payloads.SelectMany(payload => payload.GetProperty("records").EnumerateArray())
            .Select(record => record.GetProperty("id").GetInt32()));
        Assert.All(payloads, payload => Assert.InRange(payload.GetRawText().Length, 1, 60000));
    }

    [AgentPostgresFact]
    public async Task Prepared_records_and_queue_messages_commit_atomically_with_encryption_and_retry_deduplication()
    {
        var schema = "source_queue_" + Guid.NewGuid().ToString("N");
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES")) { SearchPath = schema };
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync();
        await Execute($"CREATE SCHEMA \"{schema}\"");
        try
        {
            var tenant = new FakeCurrentTenant(Guid.NewGuid());
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(settings.ConnectionString).Options;
            await using var db = new AppDbContext(options, tenant);
            await db.Database.MigrateAsync();
            typeof(PlaceContext.Infrastructure.DependencyInjection).GetMethod("EnsureRpcChainSubmissionSchema", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [db]);
            var project = Guid.NewGuid();
            var chain = Guid.NewGuid();
            var approver = Guid.NewGuid();
            db.Tenants.Add(new TenantRow { Id = tenant.TenantId, Slug = schema, Name = "Queue test" });
            db.Projects.Add(new ProjectRow { Id = project, Name = "Queue test" });
            db.JobChains.Add(new JobChainRow { Id = chain, ProjectId = project, Name = "Processing", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            db.Users.Add(new UserRow { Id = approver, Email = "queue@example.test", DisplayName = "Queue approver", Role = "Owner", CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            var encryption = new DataProtectionEncryptor(new EphemeralDataProtectionProvider());
            var permissions = new Permissions();
            var submissions = new DbJobChainSubmissionQueue(db, tenant, new CurrentUser(), encryption, permissions);
            var queues = new DbChainQueueService(db, tenant, permissions, submissions, encryption);
            CurrentUser.Set(new(approver, "Owner"));
            var queue = await queues.SaveAsync(null, project, chain, "Prepared records", true);
            var delivery = new FailingDelivery(new SourceQueueDelivery(db, queues, permissions));
            var store = new EfDataSourceStore(db, encryption, tenant, delivery);
            var source = new DataSourceDefinition(Guid.NewGuid(), project, "Events", "feed", "events", "id", ["id", "body", "email"], ["approved"], false, Guid.NewGuid(),
                Provider: "feed", Queue: new(queue.Id, ["body"], ["email"], ApprovedBy: approver));
            await store.CreateAsync(source, JsonSerializer.Serialize(new { token = new string('t', 44) }), default);
            var pipeline = new DataSourcePipeline(store, new NoReader(), new NoEmbeddings(), new FakeClock(DateTimeOffset.UtcNow));
            var rows = Enumerable.Range(1, 12).Select(id => new SourceRecord(id.ToString(), JsonSerializer.Serialize(new
            {
                id, body = " original ", email = "private@example.test", secret = "never-import"
            }))).Append(new("held", "{\"id\":\"held\",\"body\":\" \"}")).ToArray();
            // A feed request is anonymous; the saved, still-authorised approver owns the downstream submission.
            CurrentUser.Clear();
            var first = await pipeline.IngestAsync(project, source.Id, rows, [], default);
            Assert.Equal(12, first.Queued);
            Assert.Equal(1, first.Held);
            Assert.Null(CurrentUser.Current);
            Assert.Equal(2, await MessageCount());
            await using (var command = new NpgsqlCommand("SELECT \"InputPayload\" FROM rpc_chain_submissions", connection))
            {
                await using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var cipher = reader.GetString(0);
                        Assert.StartsWith(DataProtectionEncryptor.Prefix, cipher);
                        var payload = encryption.Unprotect(cipher, IDataEncryptor.Purpose.RpcChainSubmission);
                        Assert.DoesNotContain("private@example.test", payload);
                        Assert.DoesNotContain("never-import", payload);
                        Assert.Contains("[REDACTED]", payload);
                        using var json = JsonDocument.Parse(payload);
                        Assert.InRange(json.RootElement.GetProperty("records").GetArrayLength(), 1, 10);
                    }
                }
            }
            Assert.All(await store.ReadDocumentsAsync(project, source.Id, default), document => Assert.Empty(document.Chunks));
            Assert.Equal(0, (await pipeline.IngestAsync(project, source.Id, rows, [], default)).Queued);
            Assert.Equal(2, await MessageCount());

            var updated = rows.Take(12).Select(row => row with { Json = row.Json.Replace("original", "changed", StringComparison.Ordinal) }).ToArray();
            delivery.Fail = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.IngestAsync(project, source.Id, updated, [], default));
            Assert.Equal(2, await MessageCount());
            Assert.All((await store.ReadDocumentsAsync(project, source.Id, default)).Where(document => document.ValidationError is null),
                document => Assert.Contains("original", document.Json));
            Assert.Null(CurrentUser.Current);
            delivery.Fail = false;
            Assert.Equal(12, (await pipeline.IngestAsync(project, source.Id, updated, [], default)).Queued);
            Assert.Equal(4, await MessageCount());
            Assert.Equal(0, (await pipeline.IngestAsync(project, source.Id, updated, [], default)).Queued);
            Assert.Equal(4, await MessageCount());
            await pipeline.IngestAsync(project, source.Id, [], ["1"], default);
            Assert.Equal(5, await MessageCount());

            CurrentUser.Set(new(approver, "Owner"));
            await queues.SaveAsync(queue.Id, project, chain, queue.Name, false);
            CurrentUser.Clear();
            await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.IngestAsync(project, source.Id,
                [new("new", "{\"id\":\"new\",\"body\":\"waiting\"}")], [], default));
            Assert.Equal(5, await MessageCount());
            Assert.DoesNotContain(await store.ReadDocumentsAsync(project, source.Id, default), document => document.Key == "new");

            CurrentUser.Set(new(approver, "Owner"));
            await queues.SaveAsync(queue.Id, project, chain, queue.Name, true);
            CurrentUser.Clear();
            permissions.Allowed = false;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pipeline.IngestAsync(project, source.Id,
                [new("new", "{\"id\":\"new\",\"body\":\"waiting\"}")], [], default));
            Assert.Equal(5, await MessageCount());
            Assert.Null(CurrentUser.Current);
            permissions.Allowed = true;
            await Execute($"UPDATE chain_queues SET \"ProjectId\" = '{Guid.NewGuid()}' WHERE \"Id\" = '{queue.Id}'");
            await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.SyncAsync(project, source.Id, default));
            Assert.Equal(5, await MessageCount());
        }
        finally
        {
            CurrentUser.Clear();
            await Execute($"DROP SCHEMA \"{schema}\" CASCADE");
        }
        async Task Execute(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
        async Task<long> MessageCount()
        {
            await using var command = new NpgsqlCommand("SELECT count(*) FROM rpc_chain_submissions", connection);
            return (long)(await command.ExecuteScalarAsync())!;
        }
    }

    private sealed class FailingDelivery(ISourceQueueDelivery inner) : ISourceQueueDelivery
    {
        public bool Fail { get; set; }
        public async Task PublishAsync(DataSourceDefinition source, IReadOnlyList<SourceDocument> changed, IReadOnlyList<string> deletedKeys, CancellationToken ct)
        {
            await inner.PublishAsync(source, changed, deletedKeys, ct);
            if (Fail)
            {
                throw new InvalidOperationException("Injected failure after queue insertion.");
            }
        }
    }
    private sealed class NoReader : IDataSourceReader
    {
        public Task<IReadOnlyList<SourceRecord>> ReadAsync(DataSourceDefinition source, CancellationToken ct) => throw new InvalidOperationException();
    }
    private sealed class NoEmbeddings : IEmbeddingGateway
    {
        public bool IsEnabled => false;
        public int Dimensions => 0;
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) => throw new InvalidOperationException("Embedding must follow queueing.");
    }
    private sealed class Permissions : IPermissionService
    {
        public bool Allowed { get; set; } = true;
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(Allowed);
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid userId, string roleName, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
