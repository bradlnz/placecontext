using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Ports;
using PlaceContext.Application.Shared;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Security;
using PlaceContext.Infrastructure.Scheduling;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class CrmEncryptionAtRestTests
{
    [Fact]
    public async Task Ingestion_automation_queue_encrypts_raw_payload_at_rest()
    {
        var (db, encryptor) = CreateDb();
        await using (db)
        {
            var tenantId = Guid.NewGuid();
            const string payload = """{"address":"123 Example Street"}""";
            var queue = new DbCrmAutomationQueue(
                db, new FakeClock(DateTimeOffset.UtcNow), encryptor);

            await queue.EnqueueAsync(new QueuedCrmAutomation(
                tenantId, Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(),
                CrmAutomationEventType.IngestionReceived, null, "Run feasibility", payload));
            await db.SaveChangesAsync();

            var stored = await db.CrmAutomationQueue.AsNoTracking().SingleAsync();
            Assert.Null(stored.ClientId);
            Assert.Null(stored.LifecycleStage);
            AssertProtected(encryptor, stored.InputPayloadProtected, payload);
            Assert.Equal(payload, encryptor.Unprotect(
                stored.InputPayloadProtected, IDataEncryptor.Purpose.CrmAutomationPayload));
        }
    }

    [Fact]
    public async Task Client_repository_encrypts_identity_and_contact_fields_but_returns_plaintext()
    {
        var (db, encryptor) = CreateDb();
        await using (db)
        {
            var projectId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var client = CrmClient.Create(
                projectId, "Ada Lovelace", "Analytical Engines", "ada@example.com",
                "+61 400 000 001", CustomerLifecycleStage.Active,
                "Prefers correspondence by email.", now);
            var repository = new EfCrmClientRepository(db, encryptor);

            await repository.AddAsync(client);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var stored = await db.CrmClients.AsNoTracking().SingleAsync();
            AssertProtected(encryptor, stored.Name, "Ada Lovelace");
            AssertProtected(encryptor, stored.Company, "Analytical Engines");
            AssertProtected(encryptor, stored.Email, "ada@example.com");
            AssertProtected(encryptor, stored.Phone, "+61 400 000 001");
            AssertProtected(encryptor, stored.Notes, "Prefers correspondence by email.");
            Assert.Equal("Active", stored.LifecycleStage);

            var loaded = await repository.GetByIdAsync(client.Id);
            Assert.NotNull(loaded);
            Assert.Equal(client.Name, loaded.Name);
            Assert.Equal(client.Company, loaded.Company);
            Assert.Equal(client.Email, loaded.Email);
            Assert.Equal(client.Phone, loaded.Phone);
            Assert.Equal(client.Notes, loaded.Notes);

            var matched = await repository.FindByContactAsync(
                projectId, "ADA@EXAMPLE.COM", null);
            Assert.Equal(client.Id, matched?.Id);
        }
    }

    [Fact]
    public async Task Communication_artifact_and_chain_customer_data_are_encrypted()
    {
        var (db, encryptor) = CreateDb();
        await using (db)
        {
            var projectId = Guid.NewGuid();
            var clientId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var message = CrmCommunication.CreateOutbound(
                projectId, clientId, CrmCommunicationChannel.Email, "Your proposal",
                "The confidential proposal is attached.", "client@example.com",
                Guid.NewGuid(), now);
            message.MarkSent("postmark", "provider-message-123", now.AddSeconds(1));
            var communications = new EfCrmCommunicationRepository(db, encryptor);
            await communications.AddAsync(message);

            var artifact = CrmClientArtifact.CreateUpload(
                Guid.NewGuid(), projectId, clientId, "Client proposal.pdf", "reports",
                $"crm-clients/{projectId:N}/{clientId:N}/proposal.pdf", "application/pdf",
                1024, now);
            var artifacts = new EfCrmClientArtifactRepository(db, encryptor);
            await artifacts.AddAsync(artifact);

            var chainRun = ChainRun.Rehydrate(
                Guid.NewGuid(), Guid.NewGuid(), projectId, "Client follow-up",
                ChainRunStatus.Succeeded,
                new[]
                {
                    new ChainStepRun(
                        0, 0, 0, Guid.NewGuid(), "Draft proposal", Guid.NewGuid(),
                        ChainStepStatus.Succeeded, now, now.AddSeconds(1),
                        ExternalId: "provider-chain-message-456"),
                },
                "{\"client\":\"client@example.com\",\"result\":\"approved\"}",
                now, now.AddSeconds(1));
            var chainRuns = new EfChainRunRepository(db, encryptor);
            await chainRuns.AddAsync(chainRun);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var storedMessage = await db.CrmCommunications.AsNoTracking().SingleAsync();
            AssertProtected(encryptor, storedMessage.SubjectProtected, "Your proposal");
            AssertProtected(encryptor, storedMessage.BodyProtected, "The confidential proposal is attached.");
            AssertProtected(encryptor, storedMessage.RecipientProtected, "client@example.com");
            AssertProtected(encryptor, storedMessage.ExternalId, "provider-message-123");

            var storedArtifact = await db.CrmClientArtifacts.AsNoTracking().SingleAsync();
            Assert.True(encryptor.IsProtected(storedArtifact.Title));
            Assert.True(encryptor.IsProtected(storedArtifact.ObjectKey));
            Assert.DoesNotContain("proposal", storedArtifact.Title, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("proposal", storedArtifact.ObjectKey, StringComparison.OrdinalIgnoreCase);

            var storedChainRun = await db.ChainRuns.AsNoTracking().SingleAsync();
            Assert.True(encryptor.IsProtected(storedChainRun.StepsJson));
            Assert.True(encryptor.IsProtected(storedChainRun.FinalOutput));
            Assert.DoesNotContain("client@example.com", storedChainRun.FinalOutput!, StringComparison.Ordinal);

            Assert.Equal(message.Subject, (await communications.ListForClientAsync(clientId)).Single().Subject);
            Assert.Equal(message.ExternalId, (await communications.ListForClientAsync(clientId)).Single().ExternalId);
            var loadedArtifact = await artifacts.GetByIdAsync(artifact.Id);
            Assert.Equal(artifact.Title, loadedArtifact?.Title);
            Assert.Equal(artifact.ObjectKey, loadedArtifact?.ObjectKey);
            var loadedChainRun = await chainRuns.GetByIdAsync(chainRun.Id);
            Assert.Equal(chainRun.FinalOutput, loadedChainRun?.FinalOutput);
            Assert.Equal("provider-chain-message-456", loadedChainRun?.Steps.Single().ExternalId);
        }
    }

    [Fact]
    public async Task Startup_backfill_rewrites_legacy_crm_plaintext_and_is_idempotent()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var encryptor = new DataProtectionEncryptor(new EphemeralDataProtectionProvider());
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ICurrentTenant>(new FakeCurrentTenant(tenantId))
            .AddSingleton<IDataEncryptor>(encryptor)
            .AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName))
            .BuildServiceProvider();

        var clientId = Guid.NewGuid();
        var chainRunId = Guid.NewGuid();
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CrmClients.Add(new CrmClientRow
            {
                Id = clientId,
                TenantId = tenantId,
                ProjectId = Guid.NewGuid(),
                Name = "Legacy Client",
                Email = "legacy@example.com",
                Notes = "Imported before field encryption",
                LifecycleStage = "Lead",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            db.CrmCommunications.Add(new CrmCommunicationRow
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = Guid.NewGuid(),
                ClientId = clientId,
                Channel = "Email",
                BodyProtected = "Legacy message",
                RecipientProtected = "legacy@example.com",
                ExternalId = "legacy-provider-id",
                Status = "Sent",
                CreatedByUserId = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.CrmClientArtifacts.Add(new CrmClientArtifactRow
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = Guid.NewGuid(),
                ClientId = clientId,
                Title = "legacy-contract.pdf",
                Bucket = "reports",
                ObjectKey = "crm/legacy-contract.pdf",
                ContentType = "application/pdf",
                SizeBytes = 42,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.ChainRuns.Add(new ChainRunRow
            {
                Id = chainRunId,
                TenantId = tenantId,
                ChainId = Guid.NewGuid(),
                ProjectId = Guid.NewGuid(),
                ChainName = "Legacy CRM automation",
                Status = "Succeeded",
                StepsJson = "[{\"error\":\"client@example.com rejected\"}]",
                FinalOutput = "{\"email\":\"legacy@example.com\"}",
                StartedAt = DateTimeOffset.UtcNow,
                FinishedAt = DateTimeOffset.UtcNow,
            });
            db.CrmChainRuns.Add(new CrmChainRunRow
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = Guid.NewGuid(),
                ClientId = clientId,
                ChainId = Guid.NewGuid(),
                ChainRunId = chainRunId,
                LifecycleStage = "Lead",
                StartedAt = DateTimeOffset.UtcNow,
            });
            db.CrmAutomationQueue.Add(new CrmAutomationQueueRow
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                RuleId = Guid.NewGuid(),
                ClientId = clientId,
                ChainId = Guid.NewGuid(),
                EventType = "ClientUpdated",
                LifecycleStage = "Lead",
                RuleName = "Legacy rule",
                EnqueuedAt = DateTimeOffset.UtcNow,
                NextAttemptAt = DateTimeOffset.UtcNow,
                LastError = "legacy@example.com rejected",
            });
            await db.SaveChangesAsync();
        }

        await EncryptionAtRestBootstrap.RunCrmAsync(services);
        await EncryptionAtRestBootstrap.RunCrmAsync(services);

        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var client = await db.CrmClients.IgnoreQueryFilters().SingleAsync();
            AssertProtected(encryptor, client.Name, "Legacy Client");
            AssertProtected(encryptor, client.Email, "legacy@example.com");
            AssertProtected(encryptor, client.Notes, "Imported before field encryption");

            var message = await db.CrmCommunications.IgnoreQueryFilters().SingleAsync();
            AssertProtected(encryptor, message.BodyProtected, "Legacy message");
            AssertProtected(encryptor, message.RecipientProtected, "legacy@example.com");
            AssertProtected(encryptor, message.ExternalId, "legacy-provider-id");

            var artifact = await db.CrmClientArtifacts.IgnoreQueryFilters().SingleAsync();
            Assert.True(encryptor.IsProtected(artifact.Title));
            Assert.True(encryptor.IsProtected(artifact.ObjectKey));

            var chainRun = await db.ChainRuns.IgnoreQueryFilters().SingleAsync();
            AssertProtected(encryptor, chainRun.StepsJson,
                "[{\"error\":\"client@example.com rejected\"}]");
            AssertProtected(encryptor, chainRun.FinalOutput,
                "{\"email\":\"legacy@example.com\"}");

            var queued = await db.CrmAutomationQueue.SingleAsync();
            Assert.True(encryptor.IsProtected(queued.LastError));
            Assert.DoesNotContain("legacy@example.com", queued.LastError!, StringComparison.Ordinal);
        }

        await services.DisposeAsync();
    }

    private static (AppDbContext Db, IDataEncryptor Encryptor) CreateDb()
    {
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return (
            new AppDbContext(options, tenant),
            new DataProtectionEncryptor(new EphemeralDataProtectionProvider()));
    }

    private static void AssertProtected(IDataEncryptor encryptor, string? stored, string plaintext)
    {
        Assert.NotNull(stored);
        Assert.True(encryptor.IsProtected(stored));
        Assert.DoesNotContain(plaintext, stored, StringComparison.Ordinal);
    }
}
