using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Ports;
using PlaceContext.Jobs.Infrastructure.Persistence;
using PlaceContext.Jobs.Infrastructure.Security;
using PlaceContext.TestSupport;

namespace PlaceContext.Jobs.Tests;

public sealed class JobsEncryptionAtRestBootstrapTests
{
    [Fact]
    public async Task Bootstrap_encrypts_legacy_job_chain_event_and_queue_payloads()
    {
        var tenantId = Guid.NewGuid();
        var encryptor = new JobsDataProtectionEncryptor(new EphemeralDataProtectionProvider());
        var databaseName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ICurrentTenant>(new FakeCurrentTenant(tenantId))
            .AddSingleton<IDataEncryptor>(encryptor)
            .AddDbContext<JobsDbContext>(options =>
                options.UseInMemoryDatabase(databaseName))
            .BuildServiceProvider();

        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
            db.Jobs.Add(new JobRow
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = Guid.NewGuid(), Name = "job",
                MapSource = "print('legacy')", InputPayloadsJson = "[\"legacy\"]",
                MapEnvJson = "{}", CreatedAt = DateTimeOffset.UtcNow,
            });
            db.ChainRuns.Add(new ChainRunRow
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ChainId = Guid.NewGuid(),
                ProjectId = Guid.NewGuid(), ChainName = "chain", Status = "Succeeded",
                StepsJson = "[{\"legacy\":true}]", FinalOutput = "legacy-output",
                StartedAt = DateTimeOffset.UtcNow,
            });
            db.EventOccurrences.Add(new EventOccurrenceRow
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Name = "legacy.event", Source = "User",
                Payload = "legacy-event", OccurredAt = DateTimeOffset.UtcNow,
            });
            db.PendingRuns.Add(new PendingRunRow
            {
                Id = Guid.NewGuid(), TenantId = tenantId, JobId = Guid.NewGuid(),
                TriggerId = Guid.NewGuid(), TriggerName = "legacy", Payload = "legacy-pending",
                EnqueuedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await JobsEncryptionAtRestBootstrap.RunAsync(services);
        await JobsEncryptionAtRestBootstrap.RunAsync(services);

        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
            Assert.True(encryptor.IsProtected((await db.Jobs.SingleAsync()).MapSource));
            Assert.True(encryptor.IsProtected((await db.ChainRuns.SingleAsync()).FinalOutput));
            Assert.True(encryptor.IsProtected((await db.EventOccurrences.SingleAsync()).Payload));
            Assert.True(encryptor.IsProtected((await db.PendingRuns.SingleAsync()).Payload));
        }

        await services.DisposeAsync();
    }
}
