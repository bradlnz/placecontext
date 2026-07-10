using System.Text;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>
/// Runtime onboarding: an image deploy pulls+runs with the project's spec; a GitHub deploy clones,
/// tars the working tree, and builds inside the runtime with a project-scoped tag. Both refuse to
/// run without a configured runtime or for unknown projects.
/// </summary>
public class DeployContainerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 9, 17, 0, 0, TimeSpan.Zero);

    private sealed class FakeRuntime : IContainerRuntime
    {
        public bool IsEnabled { get; set; } = true;
        public (Guid ProjectId, ContainerRunSpec Spec)? Image;
        public (Guid ProjectId, ContainerRunSpec Spec, string Context)? Build;

        public Task<IReadOnlyList<ContainerInfo>> ListAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ContainerInfo>>(Array.Empty<ContainerInfo>());
        public Task<string> LogsAsync(Guid projectId, string containerId, int tail = 200, CancellationToken ct = default)
            => Task.FromResult("");
        public Task RestartAsync(Guid projectId, string containerId, CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(Guid projectId, string containerId, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeployImageAsync(Guid projectId, ContainerRunSpec spec, CancellationToken ct = default)
        { Image = (projectId, spec); return Task.CompletedTask; }

        public async Task DeployBuildAsync(Guid projectId, ContainerRunSpec spec, Stream buildContextTar, CancellationToken ct = default)
        {
            using var reader = new StreamReader(buildContextTar);
            Build = (projectId, spec, await reader.ReadToEndAsync(ct));
        }
    }

    private sealed class FakeWorkspace : ICodeWorkspace
    {
        public string? ClonedUrl, ClonedAs, SawToken;

        public Task<string> CloneAsync(string tenantSlug, string cloneUrl, string repoName, string? accessToken, CancellationToken ct = default)
        { ClonedUrl = cloneUrl; ClonedAs = repoName; SawToken = accessToken; return Task.FromResult($"/ws/{tenantSlug}/{repoName}"); }

        public Task<string> ExtractArchiveAsync(string tenantSlug, string name, Stream zipStream, CancellationToken ct = default)
            => Task.FromResult($"/ws/{tenantSlug}/{name}");

        public Task<Stream> CreateBuildContextAsync(string path, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes($"tar-of:{path}")));
    }

    private static async Task<(InMemoryProjectRepository projects, Project project)> WorldAsync()
    {
        var projects = new InMemoryProjectRepository();
        var project = Project.Discover(ProjectPath.From("/repo/demo"), ProjectName.From("demo"), T0);
        await projects.AddAsync(project);
        return (projects, project);
    }

    [Fact]
    public async Task Image_deploy_passes_the_full_spec_to_the_runtime()
    {
        var (projects, project) = await WorldAsync();
        var runtime = new FakeRuntime();

        await new DeployContainerFromImageHandler(projects, runtime).HandleAsync(
            new DeployContainerFromImageCommand(project.Id.Value, "web", "nginx:alpine", 80, 8080,
                new Dictionary<string, string> { ["MODE"] = "prod" }));

        var (pid, spec) = runtime.Image!.Value;
        Assert.Equal(project.Id.Value, pid);
        Assert.Equal(("web", "nginx:alpine", 80, 8080), (spec.Name, spec.Image, spec.ContainerPort, spec.PublishPort));
        Assert.Equal("prod", spec.Env["MODE"]);
    }

    [Fact]
    public async Task GitHub_deploy_clones_tars_and_builds_with_a_project_scoped_tag()
    {
        var (projects, project) = await WorldAsync();
        var runtime = new FakeRuntime();
        var workspace = new FakeWorkspace();

        await new DeployContainerFromGitHubHandler(projects, runtime, workspace).HandleAsync(
            new DeployContainerFromGitHubCommand(project.Id.Value, "api", "https://github.com/acme/svc.git",
                "gh-token", "acme", 3000, 3000, new Dictionary<string, string>()));

        Assert.Equal("https://github.com/acme/svc.git", workspace.ClonedUrl);
        Assert.Equal("svc", workspace.ClonedAs);                       // .git suffix stripped
        Assert.Equal("gh-token", workspace.SawToken);                  // private repos work

        var (pid, spec, context) = runtime.Build!.Value;
        Assert.Equal(project.Id.Value, pid);
        Assert.StartsWith($"placecontext/{project.Id.Value:N}-api:", spec.Image);
        Assert.Equal("tar-of:/ws/acme/svc", context);                  // the working tree reached the daemon
    }

    [Fact]
    public async Task Deploys_reject_unknown_projects_and_a_missing_runtime()
    {
        var (projects, project) = await WorldAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DeployContainerFromImageHandler(projects, new FakeRuntime()).HandleAsync(
                new DeployContainerFromImageCommand(Guid.NewGuid(), "web", "nginx", null, null,
                    new Dictionary<string, string>())));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DeployContainerFromImageHandler(projects, new FakeRuntime { IsEnabled = false }).HandleAsync(
                new DeployContainerFromImageCommand(project.Id.Value, "web", "nginx", null, null,
                    new Dictionary<string, string>())));
    }
}
