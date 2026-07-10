namespace PlaceContext.Application.Ports;

/// <summary>One container in a project's application runtime.</summary>
public sealed record ContainerInfo(
    string Id, string Name, string Image, string State, string Status,
    IReadOnlyList<string> Ports, IReadOnlyList<int> PublishedPorts);

/// <summary>How to run one app container: image (or build tag), ports, env.</summary>
public sealed record ContainerRunSpec(
    string Name,
    string Image,
    int? ContainerPort,
    int? PublishPort,
    IReadOnlyDictionary<string, string> Env);

/// <summary>
/// A project's application runtime: the containers it runs, their logs, and basic lifecycle
/// control. Isolation is the adapter's job — every operation is scoped to one project, and a
/// container that doesn't belong to that project must be invisible/untouchable through this port.
/// </summary>
public interface IContainerRuntime
{
    /// <summary>Whether a runtime daemon is configured for this deployment.</summary>
    bool IsEnabled { get; }

    /// <summary>The project's containers (running and stopped), newest-first.</summary>
    Task<IReadOnlyList<ContainerInfo>> ListAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>The tail of one container's stdout+stderr.</summary>
    Task<string> LogsAsync(Guid projectId, string containerId, int tail = 200, CancellationToken ct = default);

    Task RestartAsync(Guid projectId, string containerId, CancellationToken ct = default);

    Task StopAsync(Guid projectId, string containerId, CancellationToken ct = default);

    /// <summary>
    /// Pull the spec's image (registry access from the runtime daemon) and run it as the project's
    /// container. An existing same-name container is replaced only when it belongs to the project.
    /// </summary>
    Task DeployImageAsync(Guid projectId, ContainerRunSpec spec, CancellationToken ct = default);

    /// <summary>
    /// Build an image from the tar build context (a repo working tree with a Dockerfile) inside the
    /// runtime daemon, then run it exactly like <see cref="DeployImageAsync"/>.
    /// </summary>
    Task DeployBuildAsync(Guid projectId, ContainerRunSpec spec, Stream buildContextTar, CancellationToken ct = default);
}
