using PlaceContext.Application.Features;
using PlaceContext.Application.ProjectData;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;

namespace PlaceContext.Application.Tests;

public sealed class ProjectDataQueryTests
{
    [Fact]
    public async Task Query_uses_read_only_reader_and_rejects_unavailable_projects_and_invalid_input()
    {
        var projects = new InMemoryProjectRepository();
        var project = Project.Discover(ProjectPath.From("/data-query"), ProjectName.From("Data"), DateTimeOffset.UtcNow);
        await projects.AddAsync(project);
        var reader = new Reader();
        var handler = new QueryProjectDataHandler(projects, reader);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(new(Guid.NewGuid(), "SELECT 1")));
        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(new(project.Id.Value, " ")));
        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(new(project.Id.Value, new string('x', 32769))));
        Assert.Equal(0, reader.Calls);
        var result = await handler.HandleAsync(new(project.Id.Value, "SELECT count(*) FROM orders"));
        Assert.Equal(1, reader.Calls);
        Assert.Equal(project.Id.Value, reader.ProjectId);
        Assert.Equal("SELECT count(*) FROM orders", reader.Sql);
        Assert.Equal("8", result.Rows[0][0]);
    }

    private sealed class Reader : IProjectDataReader
    {
        public int Calls;
        public Guid ProjectId;
        public string? Sql;
        public Task<ProjectQueryResult> QueryAsync(Guid projectId, string sql, CancellationToken ct = default)
        {
            Calls++;
            ProjectId = projectId;
            Sql = sql;
            return Task.FromResult(new ProjectQueryResult(["count"], [["8"]], 0, false));
        }
    }
}
