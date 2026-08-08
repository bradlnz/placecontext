using Microsoft.EntityFrameworkCore;
using PlaceContext.Artifacts.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Artifacts.Tests;

public sealed class ArtifactsDbContextTests
{
    [Fact]
    public async Task SaveChangesAsync_UnstampedRows_StampsTenantAndIsolatesReads()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ArtifactsDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var owner = new ArtifactsDbContext(options, new FakeCurrentTenant(tenantId)))
        {
            owner.RunArtifacts.Add(new RunArtifactLinkRow
            {
                Id = artifactId,
                RunId = Guid.NewGuid(),
                JobId = Guid.NewGuid(),
                ProjectId = Guid.NewGuid(),
                Kind = "HtmlReport",
                Title = "Report",
                Bucket = "artifacts",
                ObjectKey = "report.html",
                ContentType = "text/html",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            owner.ArtifactShareTokens.Add(new ArtifactShareTokenRow
            {
                Id = Guid.NewGuid(),
                ArtifactId = artifactId,
                TokenHash = new string('a', 64),
                TokenPrefix = "artifact",
                CreatedByUserId = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            });

            await owner.SaveChangesAsync();

            Assert.Equal(tenantId, Assert.Single(owner.RunArtifacts.Local).TenantId);
            Assert.Equal(tenantId, Assert.Single(owner.ArtifactShareTokens.Local).TenantId);
        }

        await using var otherTenant = new ArtifactsDbContext(
            options,
            new FakeCurrentTenant(Guid.NewGuid()));
        Assert.Empty(await otherTenant.RunArtifacts.ToListAsync());
        Assert.Empty(await otherTenant.ArtifactShareTokens.ToListAsync());
    }
}
