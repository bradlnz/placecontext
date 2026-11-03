using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Features;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Host.Tests;

public sealed class ArtifactPagingTests
{
    [Fact]
    public async Task Artifact_pages_keep_tied_timestamps_stable_and_preserve_tenant_and_project_scopes()
    {
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options, tenant);
        var project = Guid.NewGuid();
        var otherProject = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 8; index++)
        {
            db.RunArtifacts.Add(new RunArtifactLinkRow
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.TenantId,
                ProjectId = index < 6 ? project : otherProject,
                CreatedAt = now,
                Kind = "HtmlReport",
                Title = $"file {index}"
            });
        }
        await db.SaveChangesAsync();
        var repository = new EfRunArtifactLinkRepository(db);
        var handler = new ListProjectArtifactsHandler(repository);
        var first = await handler.HandleAsync(new(project, Take: 3));
        var second = await handler.HandleAsync(new(project, Take: 3, Skip: 3));
        Assert.Equal(3, first.Count);
        Assert.Equal(3, second.Count);
        Assert.Equal(6, first.Concat(second).Select(file => file.Id).Distinct().Count());
        Assert.All(first.Concat(second), file => Assert.Equal(project, file.ProjectId));
        Assert.Empty(await handler.HandleAsync(new(project, Take: 3, Skip: 6)));
        Assert.Equal(first.Select(file => file.Id), (await handler.HandleAsync(new(project, Take: 3))).Select(file => file.Id));

        var recent = new ListRecentArtifactsHandler(repository);
        Assert.Equal(3, (await recent.HandleAsync(new(Take: 3, Skip: 3))).Count);
        await using var otherTenantDb = new AppDbContext(options, new FakeCurrentTenant(Guid.NewGuid()));
        var otherRepository = new EfRunArtifactLinkRepository(otherTenantDb);
        Assert.Empty(await otherRepository.ListRecentAsync(3, skip: 3));
        Assert.Empty(await otherRepository.ListForProjectAsync(project, 3, skip: 3));
    }
}
