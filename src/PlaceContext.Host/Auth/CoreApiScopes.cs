using System.Collections.ObjectModel;
using System.Linq;

namespace PlaceContext.Host.Auth;

/// <summary>Fine-grained scope constants for the machine-to-machine Core API.</summary>
public static class CoreApiScopes
{
    public const string Health = "coreapi.health";
    public const string ProjectsRead = "coreapi.projects.read";
    public const string ProjectsWrite = "coreapi.projects.write";
    public const string JobsRead = "coreapi.jobs.read";
    public const string JobsWrite = "coreapi.jobs.write";
    public const string JobsRun = "coreapi.jobs.run";

    public static readonly IReadOnlyList<string> All = new ReadOnlyCollection<string>(new[]
    {
        Health,
        ProjectsRead,
        ProjectsWrite,
        JobsRead,
        JobsWrite,
        JobsRun,
    });

    public static bool IsKnown(string scope)
        => All.Contains(scope, StringComparer.OrdinalIgnoreCase);
}
