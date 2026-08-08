namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// Value Object: a named output file produced by a workload step (e.g. <c>report.csv</c>), captured
/// from the container's <c>/out</c> directory alongside the primary <c>result.json</c>. Content is
/// opaque — PlaceContext never interprets it. Text files carry their content verbatim; binary files
/// (e.g. a PDF) carry base64 with <see cref="IsBinary"/> set, because the run's artifacts persist as
/// JSON strings and only base64 survives that byte-exact.
/// </summary>
public sealed record RunArtifact
{
    public RunArtifact(string name, string content, bool isBinary = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Artifact name must not be empty.", nameof(name));
        Name = name.Trim();
        Content = content ?? "";
        IsBinary = isBinary;
    }

    /// <summary>File name (relative path within /out), e.g. "report.csv" or "charts/summary.svg".</summary>
    public string Name { get; }

    /// <summary>Text content of the file — base64 when <see cref="IsBinary"/>.</summary>
    public string Content { get; }

    /// <summary>True when the file was not valid UTF-8 text and <see cref="Content"/> is base64.</summary>
    public bool IsBinary { get; }

    /// <summary>The file's original bytes, regardless of representation.</summary>
    public byte[] GetBytes() =>
        IsBinary ? Convert.FromBase64String(Content) : System.Text.Encoding.UTF8.GetBytes(Content);
}
