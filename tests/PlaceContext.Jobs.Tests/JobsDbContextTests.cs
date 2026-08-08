using Microsoft.EntityFrameworkCore;
using PlaceContext.Jobs.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Jobs.Tests;

public sealed class JobsDbContextTests
{
    [Fact]
    public async Task Tenant_owned_rows_are_stamped_and_filtered_while_pending_queue_is_global()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var tenant = new FakeCurrentTenant(tenantId);
        var options = new DbContextOptionsBuilder<JobsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new JobsDbContext(options, tenant);
        db.Jobs.Add(new JobRow
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Name = "tenant job",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.Jobs.Add(new JobRow
        {
            Id = Guid.NewGuid(),
            TenantId = otherTenantId,
            ProjectId = Guid.NewGuid(),
            Name = "other job",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.PendingRuns.Add(new PendingRunRow
        {
            Id = Guid.NewGuid(),
            TenantId = otherTenantId,
            JobId = Guid.NewGuid(),
            TriggerId = Guid.NewGuid(),
            TriggerName = "global queue",
            EnqueuedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        Assert.Single(await db.Jobs.ToListAsync());
        Assert.Equal(tenantId, (await db.Jobs.SingleAsync()).TenantId);
        Assert.Equal(2, await db.Jobs.IgnoreQueryFilters().CountAsync());
        Assert.Single(await db.PendingRuns.ToListAsync());
    }
}
