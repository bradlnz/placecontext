using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

public sealed class FakeRepoFiles : IRepoFiles
{
    public string? DocToReturn { get; set; }
    public List<(string Path, string Content)> Written { get; } = new();

    public Task<string?> ReadFirstAsync(RepoPath repo, IReadOnlyList<string> candidates, CancellationToken ct = default)
        => Task.FromResult(DocToReturn);

    public Task<string> WriteAsync(RepoPath repo, string relativePath, string content, CancellationToken ct = default)
    {
        Written.Add((relativePath, content));
        return Task.FromResult(Path.Combine(repo.Value, relativePath));
    }
}
