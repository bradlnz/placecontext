using Microsoft.EntityFrameworkCore;
using PlaceContext.Projects.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Projects.Tests;

public sealed class ProjectsDbContextTests
{
    [Fact]
    public async Task Tenant_owned_rows_are_stamped_and_filtered()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ProjectsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ProjectsDbContext(options, new FakeCurrentTenant(tenantId));
        db.Projects.Add(ProjectRow(Guid.NewGuid(), Guid.Empty, "/current"));
        db.Projects.Add(ProjectRow(Guid.NewGuid(), otherTenantId, "/other"));

        await db.SaveChangesAsync();

        var visible = await db.Projects.SingleAsync();
        Assert.Equal(tenantId, visible.TenantId);
        Assert.Equal("/current", visible.Path);
        Assert.Equal(2, await db.Projects.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public void Projects_migration_history_is_owned_by_projects_context()
    {
        var options = new DbContextOptionsBuilder<ProjectsDbContext>()
            .UseNpgsql(ProjectsPersistenceOptions.DefaultConnectionString)
            .Options;
        using var db = new ProjectsDbContext(options, new FakeCurrentTenant(Guid.NewGuid()));

        Assert.Contains("20260809081910_InitialProjectsPersistence", db.Database.GetMigrations());
    }

    private static ProjectRow ProjectRow(Guid id, Guid tenantId, string path) => new()
    {
        Id = id,
        TenantId = tenantId,
        Name = Path.GetFileName(path),
        Path = path,
        Status = "registered",
        DiscoveredAt = DateTimeOffset.UtcNow,
    };
}
