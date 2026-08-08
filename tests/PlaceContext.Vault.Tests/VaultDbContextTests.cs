using Microsoft.EntityFrameworkCore;
using PlaceContext.TestSupport;
using PlaceContext.Vault.Infrastructure.Persistence;

namespace PlaceContext.Vault.Tests;

public sealed class VaultDbContextTests
{
    [Fact]
    public async Task SaveChangesAsync_UnstampedSecret_StampsTenantAndIsolatesReads()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<VaultDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var owner = new VaultDbContext(options, new FakeCurrentTenant(tenantId)))
        {
            owner.ProjectSecrets.Add(new ProjectSecretRow
            {
                ProjectId = projectId,
                Name = "API_KEY",
                Cipher = "protected-value",
                CreatedAt = DateTimeOffset.UtcNow,
            });

            await owner.SaveChangesAsync();

            Assert.Equal(tenantId, Assert.Single(owner.ProjectSecrets.Local).TenantId);
        }

        await using (var otherTenant = new VaultDbContext(
                         options,
                         new FakeCurrentTenant(Guid.NewGuid())))
        {
            Assert.Empty(await otherTenant.ProjectSecrets.ToListAsync());
        }

        await using var ownerRead = new VaultDbContext(options, new FakeCurrentTenant(tenantId));
        var secret = Assert.Single(await ownerRead.ProjectSecrets.ToListAsync());
        Assert.Equal(projectId, secret.ProjectId);
        Assert.Equal("API_KEY", secret.Name);
    }
}
