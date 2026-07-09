using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Files;

/// <summary>Reads/writes plain files within a project's working tree (context seeding + scaffolding).</summary>
public sealed class FileRepoFiles : IRepoFiles
{
    public async Task<string?> ReadFirstAsync(ProjectPath repo, IReadOnlyList<string> candidates, CancellationToken ct = default)
    {
        foreach (var rel in candidates)
        {
            var full = Path.Combine(repo.Value, rel);
            if (File.Exists(full))
                return await File.ReadAllTextAsync(full, ct);
        }
        return null;
    }

    public async Task<string> WriteAsync(ProjectPath repo, string relativePath, string content, CancellationToken ct = default)
    {
        var full = Path.Combine(repo.Value, relativePath);
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
            // Hidden dirs (.git, .obsidian, …) are tool state, not content.
            .Where(rel => !rel.Split('/').Any(seg => seg.StartsWith('.')))
            .OrderBy(rel => rel, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(files);
    }

    public async Task<string?> ReadAsync(ProjectPath repo, string relativePath, CancellationToken ct = default)
    {
        var full = Path.Combine(repo.Value, relativePath);
        return File.Exists(full) ? await File.ReadAllTextAsync(full, ct) : null;
    }
}
