using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

/// <summary>
/// Deploy an app into the project's runtime from a public/registry Docker image: pull, then run
/// with the project label and the requested port published. Redeploying the same name replaces
/// the project's own container (never another project's).
/// </summary>
public sealed record DeployContainerFromImageCommand(
    Guid ProjectId, string Name, string Image, int? ContainerPort, int? PublishPort,
    IReadOnlyDictionary<string, string> Env) : ICommand<bool>;

/// <summary>
/// Deploy an app into the project's runtime from a GitHub repository: clone (token for private
/// repos), tar the working tree as a build context, build the image inside the runtime daemon
/// (the repo must carry a Dockerfile), then run it like the image deploy.
/// </summary>
public sealed record DeployContainerFromGitHubCommand(
    Guid ProjectId, string Name, string RepoUrl, string? AccessToken, string TenantSlug,
    int? ContainerPort, int? PublishPort, IReadOnlyDictionary<string, string> Env) : ICommand<bool>;

public sealed class DeployContainerFromImageHandler : ICommandHandler<DeployContainerFromImageCommand, bool>
{
    private readonly IProjectRepository _projects;
    private readonly IContainerRuntime _runtime;
    public DeployContainerFromImageHandler(IProjectRepository projects, IContainerRuntime runtime)
    { _projects = projects; _runtime = runtime; }

    public async Task<bool> HandleAsync(DeployContainerFromImageCommand c, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(_projects, c.ProjectId, ct);
        if (!_runtime.IsEnabled) throw new InvalidOperationException("No application runtime is configured.");
        if (string.IsNullOrWhiteSpace(c.Name)) throw new ArgumentException("Give the app a name.", nameof(c));
        if (string.IsNullOrWhiteSpace(c.Image)) throw new ArgumentException("An image reference is required.", nameof(c));
        await _runtime.DeployImageAsync(c.ProjectId,
            new ContainerRunSpec(c.Name.Trim(), c.Image.Trim(), c.ContainerPort, c.PublishPort, c.Env), ct);
        return true;
    }
}

public sealed class DeployContainerFromGitHubHandler : ICommandHandler<DeployContainerFromGitHubCommand, bool>
{
    private readonly IProjectRepository _projects;
    private readonly IContainerRuntime _runtime;
    private readonly ICodeWorkspace _workspace;
    public DeployContainerFromGitHubHandler(IProjectRepository projects, IContainerRuntime runtime, ICodeWorkspace workspace)
    { _projects = projects; _runtime = runtime; _workspace = workspace; }

    public async Task<bool> HandleAsync(DeployContainerFromGitHubCommand c, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(_projects, c.ProjectId, ct);
        if (!_runtime.IsEnabled) throw new InvalidOperationException("No application runtime is configured.");
        if (string.IsNullOrWhiteSpace(c.Name)) throw new ArgumentException("Give the app a name.", nameof(c));
        if (string.IsNullOrWhiteSpace(c.RepoUrl)) throw new ArgumentException("A repository URL is required.", nameof(c));

        var repoName = c.RepoUrl.TrimEnd('/').Split('/')[^1];
        if (repoName.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repoName = repoName[..^4];
        var path = await _workspace.CloneAsync(c.TenantSlug, c.RepoUrl, repoName, c.AccessToken, ct);

        // The built image is project-scoped and name-scoped so redeploys overwrite their own tag.
        var tag = $"placecontext/{c.ProjectId:N}-{c.Name.Trim().ToLowerInvariant()}:latest";
        await using var context = await _workspace.CreateBuildContextAsync(path, ct);
        await _runtime.DeployBuildAsync(c.ProjectId,
            new ContainerRunSpec(c.Name.Trim(), tag, c.ContainerPort, c.PublishPort, c.Env), context, ct);
        return true;
    }
}
