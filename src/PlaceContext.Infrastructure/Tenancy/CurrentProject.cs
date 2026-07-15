using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Tenancy;

/// <summary>
/// Ambient (AsyncLocal) holder for the current project — same shape as <see cref="CurrentTenant"/>.
/// Set by <c>ProjectResolutionMiddleware</c> when the caller supplies <c>X-Project-Id</c> /
/// <c>X-Project</c> (or query equivalents).
/// </summary>
public sealed class CurrentProject : ICurrentProject
{
    private static readonly AsyncLocal<ProjectScope?> _current = new();

    public static ProjectScope? Current => _current.Value;

    public static void Set(Guid id, string name) => _current.Value = new ProjectScope(id, name);
    public static void Clear() => _current.Value = null;

    public Guid? ProjectId => _current.Value?.Id;
    public string? ProjectName => _current.Value?.Name;
    public bool IsResolved => _current.Value is not null;

    public sealed record ProjectScope(Guid Id, string Name);
}
