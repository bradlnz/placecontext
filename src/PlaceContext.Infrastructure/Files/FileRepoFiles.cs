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
}
