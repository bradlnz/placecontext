namespace PlaceContext.Application.Ports;

/// <summary>
/// A named output file captured from a container's /out directory. Text files carry their content
/// verbatim in <paramref name="Content"/>; binary files (anything that is not valid UTF-8 text, e.g.
/// a PDF) carry base64 in <paramref name="Content"/> with <paramref name="IsBinary"/> set — the
/// pipeline persists strings, so base64 is the only representation that survives it byte-exact.
/// </summary>
public sealed record WorkloadArtifact(string Name, string Content, bool IsBinary = false)
{
    /// <summary>Builds an artifact from raw file bytes: UTF-8 text stays text, everything else becomes base64.</summary>
    public static WorkloadArtifact FromBytes(string name, byte[] bytes) =>
        Utf8Text.TryDecode(bytes, out var text)
            ? new WorkloadArtifact(name, text)
            : new WorkloadArtifact(name, Convert.ToBase64String(bytes), IsBinary: true);

    /// <summary>The artifact's original file bytes, regardless of representation.</summary>
    public byte[] GetBytes() =>
        IsBinary ? Convert.FromBase64String(Content) : System.Text.Encoding.UTF8.GetBytes(Content);
}
