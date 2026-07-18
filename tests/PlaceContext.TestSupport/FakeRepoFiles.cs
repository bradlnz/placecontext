using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

public sealed class FakeRepoFiles : IRepoFiles
{
    public List<(string Path, string Content)> Written { get; } = new();

    public Task<string> WriteAsync(ProjectPath repo, string relativePath, string content, CancellationToken ct = default)
    {
        Written.Add((relativePath, content));
        return Task.FromResult(Path.Combine(repo.Value, relativePath));
    }

    /// <summary>Relative path → content, backing ListAsync/ReadAsync. Hidden dirs skipped like the real adapter.</summary>
    public Dictionary<string, string> Files { get; } = new();

    public Task<IReadOnlyList<string>> ListAsync(ProjectPath repo, string extension, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(Files.Keys
            .Where(p => p.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                        && !p.Split('/').Any(seg => seg.StartsWith('.')))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList());

    public Task<string?> ReadAsync(ProjectPath repo, string relativePath, CancellationToken ct = default)
        => Task.FromResult(Files.TryGetValue(relativePath, out var c) ? c : null);
}
