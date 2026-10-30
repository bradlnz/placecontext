using Microsoft.EntityFrameworkCore;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class TenantWriteIsolationTests
{
    private readonly DbContextOptions<AppDbContext> _options = new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    [Fact]
    public async Task Reads_and_all_save_overloads_enforce_tenant_ownership()
    {
        var owner = Guid.NewGuid();
        await using var db = new AppDbContext(_options, new FakeCurrentTenant(owner));
        db.Jobs.Add(new() { Id = Guid.NewGuid(), Name = "Owner job" });
        await db.SaveChangesAsync();
        Assert.Equal(owner, (await db.Jobs.SingleAsync()).TenantId);
        await using var other = new AppDbContext(_options, new FakeCurrentTenant(Guid.NewGuid()));
        Assert.Empty(await other.Jobs.ToListAsync());
        other.Jobs.Add(new() { Id = Guid.NewGuid(), TenantId = owner });
        Assert.Throws<UnauthorizedAccessException>(() => other.SaveChanges());
        Assert.Throws<UnauthorizedAccessException>(() => other.SaveChanges(false));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => other.SaveChangesAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => other.SaveChangesAsync(false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Forged_detached_update_or_delete_cannot_change_another_tenants_row(bool delete)
    {
        var id = Guid.NewGuid(); var owner = Guid.NewGuid(); var attacker = Guid.NewGuid();
        await using (var db = new AppDbContext(_options, new FakeCurrentTenant(owner)))
        {
            db.Jobs.Add(new() { Id = id, Name = "Private" });
            await db.SaveChangesAsync();
        }
        await using (var db = new AppDbContext(_options, new FakeCurrentTenant(attacker)))
        {
            var forged = new JobRow { Id = id, TenantId = attacker, Name = "Overwritten" };
            if (delete) db.Remove(forged); else db.Update(forged);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
        }
        await using var check = new AppDbContext(_options, new FakeCurrentTenant(owner));
        Assert.Equal("Private", (await check.Jobs.SingleAsync()).Name);
    }

    [Fact]
    public async Task Missing_tenant_and_ownership_changes_are_rejected()
    {
        await using var unresolved = new AppDbContext(_options, new FakeCurrentTenant(Guid.Empty));
        unresolved.Jobs.Add(new() { Id = Guid.NewGuid() });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => unresolved.SaveChangesAsync());
        await using var db = new AppDbContext(_options, new FakeCurrentTenant(Guid.NewGuid()));
        var job = new JobRow { Id = Guid.NewGuid() }; db.Jobs.Add(job); await db.SaveChangesAsync();
        job.TenantId = Guid.NewGuid();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => db.SaveChangesAsync());
        Assert.All(db.Model.GetEntityTypes().Where(e => typeof(ITenantOwned).IsAssignableFrom(e.ClrType)),
            entity => Assert.True(entity.FindProperty(nameof(ITenantOwned.TenantId))!.IsConcurrencyToken));
    }
}
