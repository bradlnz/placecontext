using Microsoft.EntityFrameworkCore;
using PlaceContext.Crm.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Crm.Tests;

public sealed class CrmDbContextTests
{
    [Fact]
    public async Task Tenant_owned_rows_are_stamped_and_filtered_while_automation_queue_is_global()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<CrmDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new CrmDbContext(options, new FakeCurrentTenant(tenantId));
        db.CrmClients.Add(new CrmClientRow
        {
            Id = Guid.NewGuid(), ProjectId = Guid.NewGuid(), Name = "tenant client",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.CrmClients.Add(new CrmClientRow
        {
            Id = Guid.NewGuid(), TenantId = otherTenantId, ProjectId = Guid.NewGuid(),
            Name = "other client", CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.CrmAutomationQueue.Add(new CrmAutomationQueueRow
        {
            Id = Guid.NewGuid(), TenantId = otherTenantId, ProjectId = Guid.NewGuid(),
            RuleId = Guid.NewGuid(), ChainId = Guid.NewGuid(), EventType = "ClientUpdated",
            RuleName = "global queue", EnqueuedAt = DateTimeOffset.UtcNow,
            NextAttemptAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        Assert.Single(await db.CrmClients.ToListAsync());
        Assert.Equal(tenantId, (await db.CrmClients.SingleAsync()).TenantId);
        Assert.Equal(2, await db.CrmClients.IgnoreQueryFilters().CountAsync());
        Assert.Single(await db.CrmAutomationQueue.ToListAsync());
    }
}
