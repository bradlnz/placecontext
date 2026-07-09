using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>
/// The Runtime tab: container queries and lifecycle commands are project-guarded and pass through
/// the runtime port; with no runtime configured the list is simply empty (no error).
/// </summary>
public class ProjectRuntimeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeRuntime : IContainerRuntime
    {
        public bool IsEnabled { get; set; } = true;
        public Guid? SawProject;
        public string? Restarted, Stopped;

        public Task<IReadOnlyList<ContainerInfo>> ListAsync(Guid projectId, CancellationToken ct = default)
        {
            SawProject = projectId;
            return Task.FromResult<IReadOnlyList<ContainerInfo>>(new[]
            {
                new ContainerInfo("abc123", "web", "myapp:latest", "running", "Up 2 hours", new[] { "8080→80/tcp" }, new[] { 8080 }),
            });
        }

        public Task<string> LogsAsync(Guid projectId, string containerId, int tail = 200, CancellationToken ct = default)
        { SawProject = projectId; return Task.FromResult($"log tail of {containerId} ({tail} lines)"); }

        public Task RestartAsync(Guid projectId, string containerId, CancellationToken ct = default)
        { SawProject = projectId; Restarted = containerId; return Task.CompletedTask; }

        public Task StopAsync(Guid projectId, string containerId, CancellationToken ct = default)
        { SawProject = projectId; Stopped = containerId; return Task.CompletedTask; }
    }

    private static async Task<(InMemoryProjectRepository projects, Project project)> WorldAsync()
    {
        var projects = new InMemoryProjectRepository();
        var project = Project.Discover(ProjectPath.From("/repo/demo"), ProjectName.From("demo"), T0);
        await projects.AddAsync(project);
        return (projects, project);
    }

    [Fact]
    public async Task Containers_logs_and_lifecycle_reach_the_runtime_for_a_real_project()
    {
        var (projects, project) = await WorldAsync();
        var runtime = new FakeRuntime();
        var pid = project.Id.Value;

        var list = await new ListProjectContainersHandler(projects, runtime).HandleAsync(new ListProjectContainersQuery(pid));
        var logs = await new GetContainerLogsHandler(projects, runtime).HandleAsync(new GetContainerLogsQuery(pid, "abc123", 100));
        await new RestartProjectContainerHandler(projects, runtime).HandleAsync(new RestartProjectContainerCommand(pid, "abc123"));
        await new StopProjectContainerHandler(projects, runtime).HandleAsync(new StopProjectContainerCommand(pid, "abc123"));

        Assert.Equal(pid, runtime.SawProject);
        Assert.Single(list);
        Assert.Equal("web", list[0].Name);
        Assert.Contains("abc123", logs);
        Assert.Equal("abc123", runtime.Restarted);
        Assert.Equal("abc123", runtime.Stopped);
    }

    [Fact]
    public async Task No_runtime_configured_means_an_empty_list_not_an_error()
    {
        var (projects, project) = await WorldAsync();
        var runtime = new FakeRuntime { IsEnabled = false };

        var list = await new ListProjectContainersHandler(projects, runtime)
            .HandleAsync(new ListProjectContainersQuery(project.Id.Value));

        Assert.Empty(list);
        Assert.Null(runtime.SawProject); // the port was never touched
    }

    [Fact]
    public async Task Runtime_ops_reject_an_unknown_project()
    {
        var (projects, _) = await WorldAsync();
        var runtime = new FakeRuntime();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ListProjectContainersHandler(projects, runtime)
                .HandleAsync(new ListProjectContainersQuery(Guid.NewGuid())));
        Assert.Null(runtime.SawProject);
    }
}
