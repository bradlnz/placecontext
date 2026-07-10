using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>Clones repositories into a per-tenant local workspace for project creation.</summary>
public interface ICodeWorkspace
{
    /// <summary>Clones (or updates) a repo into the tenant's workspace and returns the local path.</summary>
    Task<string> CloneAsync(string tenantSlug, string cloneUrl, string repoName, string? accessToken, CancellationToken ct = default);

    /// <summary>
    /// Extracts an uploaded archive (e.g. an Obsidian vault .zip) into the tenant's workspace and
    /// returns the local path. The adapter must refuse entries that escape the target directory.
    /// </summary>
    Task<string> ExtractArchiveAsync(string tenantSlug, string name, Stream zipStream, CancellationToken ct = default);
}
