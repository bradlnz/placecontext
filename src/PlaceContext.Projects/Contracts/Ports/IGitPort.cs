using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>Scoped git operations against a project repository.</summary>
public interface IGitPort
{
    bool IsRepository(ProjectPath path);
    Task<CommitSha?> CommitScopedAsync(ProjectPath path, IReadOnlyList<string> files, string message, Author author, CancellationToken ct = default);
    Task<DateTimeOffset?> GetFileLastModifiedAsync(ProjectPath path, string relativeFile, CancellationToken ct = default);
    /// <summary>Reads up to <paramref name="limit"/> recent non-merge commits (newest first) for ledger backfill.</summary>
    Task<IReadOnlyList<CommitInfo>> GetRecentCommitsAsync(ProjectPath path, int limit, CancellationToken ct = default);
}
