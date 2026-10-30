using Microsoft.EntityFrameworkCore;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.TestSupport;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Tests;

public sealed class ReportNameLookupTests
{
    [Fact]
    public async Task Batch_names_are_tenant_filtered_and_do_not_hydrate_source_or_graph_payloads()
    {
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options, tenant);
        var projectId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        db.Projects.Add(new ProjectRow { Id = projectId, Name = "Project", Path = "/project", GraphJson = "not graph JSON" });
        db.Jobs.Add(new JobRow { Id = jobId, ProjectId = projectId, Name = "Job", MapSourceKind = "not a workload" });
        await db.SaveChangesAsync();
        var jobs = new EfJobRepository(db, null!);
        var projects = new EfProjectRepository(db);
        Assert.Equal("Job", Assert.Single(await jobs.GetNamesAsync([jobId, Guid.NewGuid()])).Value);
        Assert.Equal("Project", Assert.Single(await projects.GetNamesAsync([projectId, Guid.NewGuid()])).Value);
        Assert.True(await projects.ExistsAsync(ProjectId.From(projectId)));
        Assert.False(await projects.ExistsAsync(ProjectId.From(Guid.NewGuid())));
        Assert.Empty(await jobs.GetNamesAsync([]));
        Assert.Empty(await projects.GetNamesAsync([]));

        await using var otherDb = new AppDbContext(options, new FakeCurrentTenant(Guid.NewGuid()));
        Assert.Empty(await new EfJobRepository(otherDb, null!).GetNamesAsync([jobId]));
        Assert.Empty(await new EfProjectRepository(otherDb).GetNamesAsync([projectId]));
        Assert.False(await new EfProjectRepository(otherDb).ExistsAsync(ProjectId.From(projectId)));
    }
}
