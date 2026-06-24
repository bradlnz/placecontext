using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>Scoped git operations against a project repository.</summary>
public interface IGitPort
{
    bool IsRepository(RepoPath path);
    Task<CommitSha?> CommitScopedAsync(RepoPath path, IReadOnlyList<string> files, string message, Author author, CancellationToken ct = default);
    Task<DateTimeOffset?> GetFileLastModifiedAsync(RepoPath path, string relativeFile, CancellationToken ct = default);
    /// <summary>Reads up to <paramref name="limit"/> recent non-merge commits (newest first) for ledger backfill.</summary>
    Task<IReadOnlyList<CommitInfo>> GetRecentCommitsAsync(RepoPath path, int limit, CancellationToken ct = default);
}
