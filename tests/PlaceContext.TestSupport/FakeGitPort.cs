using System.Collections.Concurrent;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

public sealed class FakeGitPort : IGitPort
{
    public string ShaToReturn { get; set; } = "abc1234";
    public bool IsRepo { get; set; } = true;
    public IReadOnlyList<CommitInfo> Commits { get; set; } = Array.Empty<CommitInfo>();

    public bool IsRepository(RepoPath path) => IsRepo;

    public Task<CommitSha?> CommitScopedAsync(
        RepoPath path, IReadOnlyList<string> files, string message, Author author, CancellationToken ct = default)
        => Task.FromResult<CommitSha?>(CommitSha.From(ShaToReturn));

    public Task<DateTimeOffset?> GetFileLastModifiedAsync(RepoPath path, string relativeFile, CancellationToken ct = default)
        => Task.FromResult<DateTimeOffset?>(null);

    public Task<IReadOnlyList<CommitInfo>> GetRecentCommitsAsync(RepoPath path, int limit, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CommitInfo>>(Commits.Take(limit).ToList());
}
