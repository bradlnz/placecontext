using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Jobs;
using PlaceContext.Infrastructure.Auth;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class JobDataTokenServiceTests
{
    [Fact]
    public async Task Credential_is_bound_to_tenant_project_job_and_active_run()
    {
        await using var db = CreateDatabase(out var tenant, out var clock);
        var run = await AddRun(db, clock);
        var protection = new EphemeralDataProtectionProvider();
        var service = new JobDataTokenService(db, protection, tenant, clock, new Permissions());
        var token = await service.CreateAsync(run.Id);
        var grant = await service.ValidateAsync(token);
        Assert.NotNull(grant);
        Assert.Equal(run.ProjectId, grant.ProjectId);
        Assert.Equal(run.JobId, grant.JobId);
        Assert.Equal(run.Id, grant.RunId);
        Assert.Equal(tenant.TenantId, grant.TenantId);
        Assert.DoesNotContain(run.ProjectId.ToString(), token);

        var otherTenant = new JobDataTokenService(db, protection, new FakeCurrentTenant(Guid.NewGuid()), clock, new Permissions());
        Assert.Null(await otherTenant.ValidateAsync(token));
        Assert.Null(await service.ValidateAsync(token[..^8] + "tampered"));
        Assert.Null(await service.ValidateAsync("pct_personal-token"));
        Assert.Null(await service.ValidateAsync("pc_job_not-valid-base64"));
        Assert.Null(await service.ValidateAsync(new string('x', 4097)));

        run.ProjectId = Guid.NewGuid();
        await db.SaveChangesAsync();
        Assert.Null(await service.ValidateAsync(token));
    }

    [Theory]
    [InlineData("Succeeded")]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    public async Task Terminal_runs_immediately_lose_access(string status)
    {
        await using var db = CreateDatabase(out var tenant, out var clock);
        var run = await AddRun(db, clock);
        var service = new JobDataTokenService(db, new EphemeralDataProtectionProvider(), tenant, clock, new Permissions());
        var token = await service.CreateAsync(run.Id);
        run.Status = status;
        await db.SaveChangesAsync();
        Assert.Null(await service.ValidateAsync(token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(run.Id));
    }

    [Fact]
    public async Task Expired_credentials_and_stale_heartbeats_are_rejected()
    {
        await using var db = CreateDatabase(out var tenant, out var clock);
        var run = await AddRun(db, clock);
        var service = new JobDataTokenService(db, new EphemeralDataProtectionProvider(), tenant, clock, new Permissions());
        var token = await service.CreateAsync(run.Id);
        clock.UtcNow = clock.UtcNow.AddSeconds(IJobRunHeartbeat.TimeoutSeconds);
        Assert.Null(await service.ValidateAsync(token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(run.Id));
        run.HeartbeatAt = clock.UtcNow;
        await db.SaveChangesAsync();
        Assert.NotNull(await service.ValidateAsync(token));
        clock.UtcNow = clock.UtcNow.AddHours(24);
        run.HeartbeatAt = clock.UtcNow;
        await db.SaveChangesAsync();
        Assert.Null(await service.ValidateAsync(token));
    }

    [Fact]
    public async Task Minting_requires_data_read_and_an_existing_run_in_current_tenant()
    {
        await using var db = CreateDatabase(out var tenant, out var clock);
        var run = await AddRun(db, clock);
        var protection = new EphemeralDataProtectionProvider();
        var denied = new JobDataTokenService(db, protection, tenant, clock, new Permissions(false));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.CreateAsync(run.Id));
        var service = new JobDataTokenService(db, protection, tenant, clock, new Permissions());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(Guid.NewGuid()));
        await using var otherDb = new AppDbContext(db.ContextOptions, new FakeCurrentTenant(Guid.NewGuid()));
        var outsider = new JobDataTokenService(otherDb, protection, new FakeCurrentTenant(Guid.NewGuid()), clock, new Permissions());
        await Assert.ThrowsAsync<InvalidOperationException>(() => outsider.CreateAsync(run.Id));
    }

    [Fact]
    public async Task Source_sync_requires_data_write_and_is_bound_to_a_source_in_the_run_project()
    {
        await using var db = CreateDatabase(out var tenant, out var clock);
        var run = await AddRun(db, clock);
        var source = new DataSourceRow { Id = Guid.NewGuid(), ProjectId = run.ProjectId, Version = Guid.NewGuid() };
        var foreign = new DataSourceRow { Id = Guid.NewGuid(), ProjectId = Guid.NewGuid(), Version = Guid.NewGuid() };
        db.DataSources.AddRange(source, foreign);
        await db.SaveChangesAsync();
        var protection = new EphemeralDataProtectionProvider();
        var reader = new JobDataTokenService(db, protection, tenant, clock, new Permissions());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.CreateAsync(run.Id, sourceId: source.Id));
        var writer = new JobDataTokenService(db, protection, tenant, clock, new Permissions(write: true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.CreateAsync(run.Id, sourceId: foreign.Id));
        var grant = await writer.ValidateAsync(await writer.CreateAsync(run.Id, sourceId: source.Id));
        Assert.Equal(source.Id, grant!.SourceId);
        Assert.Null((await reader.ValidateAsync(await reader.CreateAsync(run.Id)))!.SourceId);
    }

    private static AppDbContext CreateDatabase(out FakeCurrentTenant tenant, out FakeClock clock)
    {
        tenant = new FakeCurrentTenant(Guid.NewGuid());
        clock = new FakeClock(DateTimeOffset.Parse("2026-11-03T00:00:00Z"));
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
    }

    private static async Task<JobRunRow> AddRun(AppDbContext db, FakeClock clock)
    {
        var row = new JobRunRow
        {
            Id = Guid.NewGuid(), JobId = Guid.NewGuid(), ProjectId = Guid.NewGuid(),
            Status = "Running", StartedAt = clock.UtcNow, HeartbeatAt = clock.UtcNow
        };
        db.JobRuns.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    private sealed class Permissions(bool allowed = true, bool write = false) : IPermissionService
    {
        public Task<bool> HasAsync(string permission, CancellationToken ct = default)
            => Task.FromResult(allowed && (permission == Permission.DataRead || (write && permission == Permission.DataWrite)));
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid userId, string roleName, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
