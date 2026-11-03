using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlaceContext.Application.DataSources;
using PlaceContext.Application.Search;
using PlaceContext.Infrastructure.DataSources;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Security;
using PlaceContext.Infrastructure.Tests.AgentWorkflows;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class DataSourceStoreTests
{
    [Theory]
    [InlineData("Host=/tmp;Database=source;Username=reader;Password=private")]
    [InlineData("Host=localhost;Database=source;Username=reader;Passfile=/etc/shadow")]
    [InlineData("Host=localhost;Database=source;Username=reader;Password=private;SSL Key=/etc/private")]
    [InlineData("Host=localhost;Database=source;Username=reader")]
    public async Task Unsafe_connection_options_are_rejected_without_echoing_credentials(string connection)
    {
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
        var store = new EfDataSourceStore(db, new DataProtectionEncryptor(new EphemeralDataProtectionProvider()), tenant);
        var error = await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(Definition("public"), connection, default));
        Assert.DoesNotContain("private", error.Message);
        Assert.Empty(db.DataSources);
    }

    [AgentPostgresFact]
    public async Task Postgres_ingestion_encrypts_reconciles_and_isolates_data_and_rejects_stale_commits()
    {
        var schema = "sources_test_" + Guid.NewGuid().ToString("N");
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
            var source = Definition(schema);
            db.Tenants.Add(new TenantRow { Id = tenant.TenantId, Slug = schema, Name = "Source test" });
            db.Projects.Add(new ProjectRow { Id = source.ProjectId, Name = "Source test" });
            await db.SaveChangesAsync();
            await Execute("CREATE TABLE notes (id integer PRIMARY KEY, body text, secret text); INSERT INTO notes VALUES (1, 'kiwi', 'never-import'), (2, 'pear', 'never-import')");
            var encryption = new DataProtectionEncryptor(new EphemeralDataProtectionProvider());
            var store = new EfDataSourceStore(db, encryption, tenant);
            var sourceConnection = new NpgsqlConnectionStringBuilder
            {
                Host = settings.Host, Port = settings.Port, Database = settings.Database,
                Username = settings.Username, Password = settings.Password, SslMode = settings.SslMode
            }.ConnectionString;
            await store.CreateAsync(source, sourceConnection, default);
            var row = Assert.Single(await db.DataSources.ToListAsync());
            Assert.StartsWith(DataProtectionEncryptor.Prefix, row.ConnectionCipher);
            Assert.DoesNotContain(settings.Password!, row.ConnectionCipher);
            Assert.StartsWith(DataProtectionEncryptor.Prefix, row.DefinitionCipher);
            var pipeline = new DataSourcePipeline(store, new DatabaseSourceReader(db, encryption, tenant), new NoEmbeddings(), new FakeClock(DateTimeOffset.UtcNow));
            Assert.Equal(2, (await pipeline.SyncAsync(source.ProjectId, source.Id, default)).Total);
            Assert.All(await db.SourceDocuments.ToListAsync(), document =>
            {
                Assert.StartsWith(DataProtectionEncryptor.Prefix, document.ContentCipher);
                Assert.DoesNotContain("kiwi", document.ContentCipher);
                Assert.DoesNotContain("pear", document.ContentCipher);
            });
            Assert.All(await store.ReadDocumentsAsync(source.ProjectId, source.Id, default), document => Assert.DoesNotContain("never-import", document.Json));
            var hit = Assert.Single((await pipeline.SearchAsync(source.ProjectId, source.Id, "kiwi", 10, default)).Hits);
            Assert.Equal("1", hit.RecordKey);
            await using (var other = new AppDbContext(options, new FakeCurrentTenant(Guid.NewGuid())))
            {
                var otherStore = new EfDataSourceStore(other, encryption, new FakeCurrentTenant(Guid.NewGuid()));
                Assert.Empty(await otherStore.ListAsync(source.ProjectId, default));
                await Assert.ThrowsAsync<InvalidOperationException>(() => otherStore.ReadDocumentsAsync(source.ProjectId, source.Id, default));
            }
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetAsync(Guid.NewGuid(), source.Id, default));
            await Execute("UPDATE notes SET body = 'apple' WHERE id = 1; DELETE FROM notes WHERE id = 2");
            var stale = await store.GetAsync(source.ProjectId, source.Id, default);
            var next = await pipeline.SyncAsync(source.ProjectId, source.Id, default);
            Assert.Equal(1, next.Changed);
            Assert.Equal(1, next.Deleted);
            Assert.Empty((await pipeline.SearchAsync(source.ProjectId, source.Id, "kiwi pear", 10, default)).Hits);
            await using (var staleDb = new AppDbContext(options, tenant))
            {
                var staleStore = new EfDataSourceStore(staleDb, encryption, tenant);
                await Assert.ThrowsAsync<InvalidOperationException>(() => staleStore.CommitAsync(stale, [], DateTimeOffset.UtcNow, default));
                await Assert.ThrowsAsync<InvalidOperationException>(() => staleStore.UpdateAsync(stale with { Execution = null }, default));
                var current = await staleStore.GetAsync(source.ProjectId, source.Id, default);
                Assert.Equal("completed", current.Execution!.Status);
                Assert.Equal(1, current.Execution.Processed);
                var updated = await staleStore.UpdateAsync(current with { Processing = new("Useful notes") }, default);
                Assert.NotEqual(current.Version, updated.Version);
                Assert.Equal("Useful notes", updated.Processing!.Criteria);
                Assert.Single(await staleStore.ReadDocumentsAsync(source.ProjectId, source.Id, default));
                await staleStore.UpdateAsync(updated with { Processing = null }, default);
            }
            Assert.Single(await store.ReadDocumentsAsync(source.ProjectId, source.Id, default));
            await Execute("INSERT INTO notes SELECT generate_series(2, 1002), 'overflow', 'secret'");
            await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.SyncAsync(source.ProjectId, source.Id, default));
            Assert.Single(await store.ReadDocumentsAsync(source.ProjectId, source.Id, default));
            await store.DeleteAsync(source.ProjectId, source.Id, default);
            Assert.Empty(await db.SourceDocuments.ToListAsync());
            Assert.Empty(await store.ListAsync(source.ProjectId, default));
        }
        finally
        {
            await Execute($"DROP SCHEMA \"{schema}\" CASCADE");
        }
        async Task Execute(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static DataSourceDefinition Definition(string schema)
        => new(Guid.NewGuid(), Guid.NewGuid(), "Notes", schema, "notes", "id", ["id", "body"], ["approved"], false, Guid.NewGuid());

    private sealed class NoEmbeddings : IEmbeddingGateway
    {
        public bool IsEnabled => false;
        public int Dimensions => 0;
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            => throw new InvalidOperationException("No source data may leave via embeddings without consent.");
    }
}
