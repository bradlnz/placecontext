namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// Value Object: a single source file within a <see cref="WorkloadSource.CodeWorkload"/> — a relative
/// path plus its opaque text content. Materialised under <c>/work</c> at <see cref="Path"/> inside the
/// runtime sandbox (subdirectories are created as needed). PlaceContext does not parse the content.
/// </summary>
public sealed record CodeFile
{
    public CodeFile(string path, string content)
    {
        Path = Normalize(path);
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    /// <summary>
    /// Relative path within the work dir, using forward slashes (e.g. "index.js", "lib/report.js").
    /// Leading slashes are trimmed; "<c>..</c>" segments are rejected (no path traversal out of /work).
    /// </summary>
    public string Path { get; }

    /// <summary>Opaque file content.</summary>
    public string Content { get; }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("File path must not be empty.", nameof(path));

        var normalized = path.Trim().Replace('\\', '/').TrimStart('/');
        if (normalized.Length == 0)
            throw new ArgumentException("File path must not be empty.", nameof(path));
        if (normalized.Split('/').Any(seg => seg is ".."))
            throw new ArgumentException($"File path '{path}' must not contain '..' segments.", nameof(path));

        return normalized;
    }
}
