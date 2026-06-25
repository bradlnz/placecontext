namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// Value Object: a named output file produced by a workload step (e.g. <c>report.csv</c>), captured
/// from the container's <c>/out</c> directory alongside the primary <c>result.json</c>. Content is
/// opaque text — PlaceContext never interprets it.
/// </summary>
public sealed record RunArtifact
{
    public RunArtifact(string name, string content)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Artifact name must not be empty.", nameof(name));
        Name = name.Trim();
        Content = content ?? "";
    }

    /// <summary>File name (relative path within /out), e.g. "report.csv" or "charts/summary.svg".</summary>
    public string Name { get; }

    /// <summary>Text content of the file.</summary>
    public string Content { get; }
}
