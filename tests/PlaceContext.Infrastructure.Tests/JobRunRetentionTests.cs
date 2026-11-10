using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Caching;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Security;
using PlaceContext.Application.Access;

namespace PlaceContext.Infrastructure.Tests;

public sealed class JobRunRetentionTests
{
    [Fact]
    public async Task Trim_keeps_latest_terminal_runs_and_never_removes_active_or_other_tenant_runs()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new AppDbContext(options, new Tenant(tenantId));
        var start = DateTimeOffset.Parse("2026-08-02T00:00:00Z");
        for (var i = 0; i < 105; i++)
        {
            db.JobRuns.Add(Row(tenantId, start.AddMinutes(i), "Succeeded"));
        }
        var active = Row(tenantId, start.AddMinutes(-1), "Running");
        var otherTenant = Row(otherTenantId, start.AddMinutes(-2), "Succeeded");
        db.JobRuns.Add(active);
        await db.SaveChangesAsync();
        await using (var otherDb = new AppDbContext(options, new Tenant(otherTenantId)))
        {
            otherDb.JobRuns.Add(otherTenant);
            await otherDb.SaveChangesAsync();
        }

        var repository = new EfJobRunRepository(
            db,
            new DataProtectionEncryptor(new EphemeralDataProtectionProvider()),
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

    [Fact]
    public async Task Late_worker_cannot_overwrite_a_cancelled_run()
    {
        var tenant = new Tenant(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var worker = new AppDbContext(options, tenant);
        var row = Row(tenant.TenantId, DateTimeOffset.UtcNow, "Running");
        worker.JobRuns.Add(row);
        await worker.SaveChangesAsync();
        await using var canceller = new AppDbContext(options, tenant);
        var cancelled = await canceller.JobRuns.SingleAsync();
        cancelled.Status = "Cancelled";
        cancelled.FinishedAt = DateTimeOffset.UtcNow;
        await canceller.SaveChangesAsync();

        row.Status = "Succeeded";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => worker.SaveChangesAsync());
        Assert.Equal("Cancelled", await canceller.JobRuns.AsNoTracking().Select(r => r.Status).SingleAsync());
    }

    [Fact]
    public async Task Project_counts_include_all_active_runs_and_recent_history_filters_before_the_limit()
    {
        var tenant = new Tenant(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options, tenant);
        var project = Guid.NewGuid();
        var otherProject = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        void Add(Guid projectId, string status, DateTimeOffset at)
        {
            var row = Row(tenant.TenantId, at, status);
            row.ProjectId = projectId;
            db.JobRuns.Add(row);
        }
        for (var i = 0; i < 205; i++)
        {
            Add(project, "Running", now.AddDays(-2));
        }
        Add(project, "Queued", now.AddDays(-2));
        Add(project, "Succeeded", now.AddHours(-1));
        Add(project, "Failed", now.AddDays(-2));
        Add(project, "Failed", now.AddHours(-2));
        for (var i = 0; i < 60; i++)
        {
            Add(otherProject, "Running", now);
        }
        await db.SaveChangesAsync();
        var otherTenant = new Tenant(Guid.NewGuid());
        await using (var otherDb = new AppDbContext(options, otherTenant))
        {
            var row = Row(otherTenant.TenantId, now, "Running");
            row.ProjectId = project;
            otherDb.JobRuns.Add(row);
            await otherDb.SaveChangesAsync();
        }
        var repository = new EfJobRunRepository(db, new DataProtectionEncryptor(new EphemeralDataProtectionProvider()), new NullJobRunCache());
        var permissions = new Permissions();
        await using var provider = new ServiceCollection().AddApplication()
            .AddSingleton<IJobRunRepository>(repository).AddSingleton<IPermissionService>(permissions).BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<IDispatcher>();
        var counts = await dispatcher.Query(new GetProjectRunCountsQuery(project));
        Assert.Equal(205, counts["Running"]);
        Assert.Equal(1, counts["Queued"]);
        Assert.Equal(1, counts["Succeeded"]);
        Assert.Equal(1, counts["Failed"]);
        var recent = await repository.ListRecentAsync(50, projectId: project);
        Assert.Equal(50, recent.Count);
        Assert.All(recent, run => Assert.Equal(project, run.ProjectId));
        permissions.Allowed = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => dispatcher.Query(new GetProjectRunCountsQuery(project)));
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

    private sealed class Permissions : IPermissionService
    {
        public bool Allowed = true;
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(Allowed);
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid userId, string roleName, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed record Tenant(Guid TenantId) : ICurrentTenant
    {
        public string Slug => "test";
        public string TimeZoneId => "UTC";
        public bool IsResolved => true;
    }
}
