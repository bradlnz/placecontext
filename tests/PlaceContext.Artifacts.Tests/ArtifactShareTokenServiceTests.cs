using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PlaceContext.Application.Ports;
using PlaceContext.Artifacts.Infrastructure.Artifacts;
using PlaceContext.Artifacts.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Artifacts.Tests;

public sealed class ArtifactShareTokenServiceTests
{
    [Fact]
    public async Task Creates_expiring_share_and_persists_only_the_token_hash()
    {
        var (db, service, clock, artifact) = await CreateAsync();
        await using (db)
        {
            var created = await service.CreateOrRotateAsync(artifact.Id, Guid.NewGuid(), 7);

            Assert.StartsWith("pc_art_", created.Token);
            Assert.Equal(clock.UtcNow.AddDays(7), created.ExpiresAt);
            var stored = await db.ArtifactShareTokens.AsNoTracking().SingleAsync();
            Assert.Equal(ArtifactShareTokenService.Hash(created.Token), stored.TokenHash);
            Assert.DoesNotContain(created.Token, stored.TokenHash, StringComparison.Ordinal);

            var resolved = await service.ResolveAsync(created.Token);
            Assert.NotNull(resolved);
            Assert.Equal(artifact.ObjectKey, resolved.ObjectKey);
            Assert.Equal(clock.UtcNow,
                (await db.ArtifactShareTokens.AsNoTracking().SingleAsync()).LastAccessedAt);
        }
    }

    [Fact]
    public async Task Rotation_revocation_and_expiry_immediately_remove_public_access()
    {
        var (db, service, clock, artifact) = await CreateAsync();
        await using (db)
        {
            var original = await service.CreateOrRotateAsync(artifact.Id, Guid.NewGuid(), 7);
            var rotated = await service.CreateOrRotateAsync(artifact.Id, Guid.NewGuid(), 1);

            Assert.Null(await service.ResolveAsync(original.Token));
            Assert.NotNull(await service.ResolveAsync(rotated.Token));

            Assert.True(await service.RevokeAsync(artifact.Id));
            Assert.Null(await service.ResolveAsync(rotated.Token));

            var expiring = await service.CreateOrRotateAsync(artifact.Id, Guid.NewGuid(), 1);
            clock.UtcNow = clock.UtcNow.AddDays(1).AddSeconds(1);
            Assert.Null(await service.ResolveAsync(expiring.Token));
            Assert.False((await service.GetStatusAsync(artifact.Id))!.IsActive);
        }
    }

    [Fact]
    public async Task Management_is_tenant_scoped_but_a_valid_bearer_code_resolves_publicly()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<ArtifactsDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        var clock = new MutableClock { UtcNow = DateTimeOffset.Parse("2026-08-01T01:00:00Z") };
        string token;
        var artifact = MakeArtifact(tenantId);
        await using (var ownerDb = new ArtifactsDbContext(options, new FakeCurrentTenant(tenantId)))
        {
            await ownerDb.RunArtifacts.AddAsync(artifact);
            await ownerDb.SaveChangesAsync();
            token = (await new ArtifactShareTokenService(ownerDb, clock)
                .CreateOrRotateAsync(artifact.Id, Guid.NewGuid(), 7)).Token;
        }

        await using (var outsiderDb = new ArtifactsDbContext(
            options, new FakeCurrentTenant(Guid.NewGuid())))
        {
            var outsiderService = new ArtifactShareTokenService(outsiderDb, clock);
            Assert.Null(await outsiderService.GetStatusAsync(artifact.Id));
            Assert.NotNull(await outsiderService.ResolveAsync(token));
        }
    }

    private static async Task<(ArtifactsDbContext Db, ArtifactShareTokenService Service, MutableClock Clock, RunArtifactLinkRow Artifact)>
        CreateAsync()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ArtifactsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new ArtifactsDbContext(options, new FakeCurrentTenant(tenantId));
        var artifact = MakeArtifact(tenantId);
        await db.RunArtifacts.AddAsync(artifact);
        await db.SaveChangesAsync();
        var clock = new MutableClock { UtcNow = DateTimeOffset.Parse("2026-08-01T01:00:00Z") };
        return (db, new ArtifactShareTokenService(db, clock), clock, artifact);
    }

    private static RunArtifactLinkRow MakeArtifact(Guid tenantId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        RunId = Guid.NewGuid(),
        JobId = Guid.NewGuid(),
        ProjectId = Guid.NewGuid(),
        Kind = "HtmlReport",
        Title = "Customer report",
        Bucket = "reports",
        ObjectKey = "runs/report.html",
        ContentType = "text/html",
        SizeBytes = 42,
        CreatedAt = DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
    };

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }
}
