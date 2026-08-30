using System.Collections.Concurrent;
using System.Reflection;

namespace PlaceContext.Infrastructure.Workload;

/// <summary>
/// Loads workload shell-script templates that are embedded in the Infrastructure assembly at build
/// time (see the csproj <c>EmbeddedResource</c> glob). Keeps the scripts out of the C# source while
/// keeping the assembly self-contained.
/// </summary>
public static class WorkloadScriptLoader
{
    private static readonly Assembly Assembly = typeof(WorkloadScriptLoader).Assembly;
    private static readonly string Prefix = typeof(WorkloadScriptLoader).Namespace + ".Scripts.";
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    /// <summary>Loads an embedded script by its slash-separated path, e.g. <c>"python/install.sh"</c>.</summary>
    public static string Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return Cache.GetOrAdd(path, static path =>
        {
            var resourceName = Prefix + path.Replace('/', '.');
            using var stream = Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Workload script '{path}' was not found as embedded resource '{resourceName}'. " +
                    "Ensure the file is included as an EmbeddedResource in the csproj.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        });
    }
}
