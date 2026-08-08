using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Jobs.Infrastructure.Persistence;
using PlaceContext.Jobs.Infrastructure.Caching;
using PlaceContext.Jobs.Infrastructure.Security;
using Xunit;

namespace PlaceContext.Jobs.Tests;

public sealed class JobRunRetentionTests
{
    [Fact]
    public async Task Trim_keeps_latest_terminal_runs_and_never_removes_active_or_other_tenant_runs()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<JobsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new JobsDbContext(options, new Tenant(tenantId));
        var start = DateTimeOffset.Parse("2026-08-02T00:00:00Z");
        for (var i = 0; i < 105; i++)
            db.JobRuns.Add(Row(tenantId, start.AddMinutes(i), "Succeeded"));

        var active = Row(tenantId, start.AddMinutes(-1), "Running");
        var otherTenant = Row(otherTenantId, start.AddMinutes(-2), "Succeeded");
        db.JobRuns.AddRange(active, otherTenant);
        await db.SaveChangesAsync();

        var repository = new EfJobRunRepository(
            db,
            new JobsDataProtectionEncryptor(new EphemeralDataProtectionProvider()),
            new NullJobRunCache());

        var removed = await repository.TrimToLatestAsync(100);
        await db.SaveChangesAsync();

        Assert.Equal(5, removed);
        Assert.Equal(101, await db.JobRuns.CountAsync());
        Assert.NotNull(await db.JobRuns.FindAsync(active.Id));
        Assert.Null(await db.JobRuns.FirstOrDefaultAsync(r => r.Id == otherTenant.Id)); // hidden by this tenant's query filter
        Assert.Equal(1, await db.JobRuns.IgnoreQueryFilters().CountAsync(r => r.TenantId == otherTenantId));
        Assert.Equal(start.AddMinutes(5), await db.JobRuns
            .Where(r => r.Status != "Queued" && r.Status != "Running")
            .MinAsync(r => r.StartedAt));
    }

    private static JobRunRow Row(Guid tenantId, DateTimeOffset startedAt, string status) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        JobId = Guid.NewGuid(),
        ProjectId = Guid.NewGuid(),
        Status = status,
        StartedAt = startedAt,
        FinishedAt = status == "Running" ? null : startedAt.AddMinutes(1),
    };

    private sealed record Tenant(Guid TenantId) : ICurrentTenant
    {
        public string Slug => "test";
        public string TimeZoneId => "UTC";
        public bool IsResolved => true;
    }
}
