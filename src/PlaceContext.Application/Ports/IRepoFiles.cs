using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>Reads/writes plain files in a project's working tree — context seeding and skill/agent scaffolding.</summary>
public interface IRepoFiles
{
    /// <summary>Returns the contents of the first candidate file that exists (relative paths), or null.</summary>
    Task<string?> ReadFirstAsync(ProjectPath repo, IReadOnlyList<string> candidates, CancellationToken ct = default);

    /// <summary>Writes a file at a repo-relative path (creating folders) and returns its absolute path.</summary>
    Task<string> WriteAsync(ProjectPath repo, string relativePath, string content, CancellationToken ct = default);

    /// <summary>Repo-relative paths of every file with the given extension (e.g. ".md"), hidden dirs skipped.</summary>
    Task<IReadOnlyList<string>> ListAsync(ProjectPath repo, string extension, CancellationToken ct = default);

    /// <summary>The contents of one repo-relative file, or null when it doesn't exist.</summary>
    Task<string?> ReadAsync(ProjectPath repo, string relativePath, CancellationToken ct = default);
}
