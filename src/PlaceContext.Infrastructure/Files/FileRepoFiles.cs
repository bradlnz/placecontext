using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Files;

/// <summary>Reads/writes plain files within a project's working tree (skill/agent scaffolding).</summary>
public sealed class FileRepoFiles : IRepoFiles
{
    public async Task<string> WriteAsync(ProjectPath repo, string relativePath, string content, CancellationToken ct = default)
    {
        if (!TryResolve(repo.Value, relativePath, out var full))
            throw new InvalidOperationException($"Refusing to write file outside the project tree: '{relativePath}'.");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content, ct);
        return full;
    }

    public Task<IReadOnlyList<string>> ListAsync(ProjectPath repo, string extension, CancellationToken ct = default)
    {
        if (!Directory.Exists(repo.Value))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        var root = Path.GetFullPath(repo.Value);
        var files = Directory.EnumerateFiles(root, "*" + extension, SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            // Hidden directories are tool state, not content.
            .Where(rel => !rel.Split('/').Any(seg => seg.StartsWith('.')))
            .OrderBy(rel => rel, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(files);
    }

    public async Task<string?> ReadAsync(ProjectPath repo, string relativePath, CancellationToken ct = default)
    {
        if (!TryResolve(repo.Value, relativePath, out var full))
            return null;
        return File.Exists(full) ? await File.ReadAllTextAsync(full, ct) : null;
    }

    /// <summary>Resolves <paramref name="relativePath"/> under <paramref name="repoRoot"/>, rejecting
    /// path traversal that would escape the project tree.</summary>
    private static bool TryResolve(string repoRoot, string relativePath, out string fullPath)
    {
        fullPath = "";
        if (string.IsNullOrWhiteSpace(relativePath)) return false;
        var root = Path.GetFullPath(repoRoot);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
            root += Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(repoRoot, relativePath));
        // Exact root match or under root/
        if (candidate.Equals(Path.GetFullPath(repoRoot), StringComparison.Ordinal))
        {
            fullPath = candidate;
            return true;
        }
        if (!candidate.StartsWith(root, StringComparison.Ordinal))
            return false;
        fullPath = candidate;
        return true;
    }
}
