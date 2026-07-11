using System.Text.Json;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Extracts artifacts a job embedded in its returned JSON — the common convention of an
/// <c>"artifacts"</c> (or <c>"files"</c>) array whose entries name a file and carry its content
/// inline. Writing to /out is still the primary channel, but not every workload can use it
/// (in-cluster image workloads have no way to ship /out back), and job authors — human or LLM —
/// reach for this shape naturally, so PlaceContext recognises it.
///
/// Recognised entry shapes (first match wins):
///  • name: <c>filename</c> | <c>fileName</c> | <c>name</c> | <c>path</c>
///  • content: an explicit base64 field (<c>base64</c> | <c>contentBase64</c> | <c>content_base64</c>),
///    a <c>data:</c> URI (in <c>content</c> | <c>data</c>), <c>content</c>+<c>encoding:"base64"</c>,
///    or plain text (<c>content</c> | <c>text</c> | <c>body</c> | <c>data</c>).
/// Decoded base64 that is valid UTF-8 text stays text; anything else becomes a binary artifact
/// (base64 + IsBinary), exactly like a binary file captured from /out. Bounded for safety: entries
/// over 5 MB decoded and beyond the 50th are dropped, as are unsafe names (absolute, traversal).
/// </summary>
public static class EmbeddedArtifacts
{
    private const long MaxBytes = 5L * 1024 * 1024;
    // Base64 inflates ~4/3, so the raw string field must admit more chars than MaxBytes.
    private const int MaxChars = 8 * 1024 * 1024;
    private const int MaxFiles = 50;

    private static readonly string[] NameFields = { "filename", "fileName", "name", "path" };
    private static readonly string[] Base64Fields = { "base64", "contentBase64", "content_base64" };
    private static readonly string[] TextFields = { "content", "text", "body", "data" };

    /// <summary>Artifacts embedded in <paramref name="artifactJson"/>; empty when there are none
    /// (not JSON, no artifacts array, or no usable entries).</summary>
    public static IReadOnlyList<RunArtifact> Extract(string? artifactJson)
    {
        if (string.IsNullOrWhiteSpace(artifactJson)) return Array.Empty<RunArtifact>();

        JsonDocument doc;
        try { doc = JsonDocument.Parse(artifactJson); }
        catch (JsonException) { return Array.Empty<RunArtifact>(); }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Array.Empty<RunArtifact>();
            if (!TryGetArray(doc.RootElement, out var entries)) return Array.Empty<RunArtifact>();

            var artifacts = new List<RunArtifact>();
            foreach (var entry in entries.EnumerateArray())
            {
                if (artifacts.Count >= MaxFiles) break;
                if (entry.ValueKind != JsonValueKind.Object) continue;
                if (SanitizeName(FirstString(entry, NameFields)) is not { } name) continue;
                if (ResolveContent(entry) is not { } artifact) continue;
                artifacts.Add(Build(name, artifact.Bytes, artifact.Text));
            }
            return artifacts;
        }
    }

    private static bool TryGetArray(JsonElement root, out JsonElement entries)
    {
        foreach (var key in new[] { "artifacts", "files" })
            if (root.TryGetProperty(key, out entries) && entries.ValueKind == JsonValueKind.Array)
                return true;
        entries = default;
        return false;
    }

    /// <summary>The entry's content as raw bytes (binary channel) or text — null when unusable.</summary>
    private static (byte[]? Bytes, string? Text)? ResolveContent(JsonElement entry)
    {
        // 1. Explicit base64 field — authoritative; skip the entry if it doesn't decode.
        if (FirstString(entry, Base64Fields) is { } b64)
            return TryDecodeBase64(b64, out var fromB64) ? (fromB64, null) : null;

        // 2. A data: URI carries its own encoding declaration.
        foreach (var field in new[] { "content", "data" })
            if (entry.TryGetProperty(field, out var el) && el.ValueKind == JsonValueKind.String
                && el.GetString() is { } uri && uri.StartsWith("data:", StringComparison.Ordinal))
            {
                var comma = uri.IndexOf(',');
                if (comma < 0) return null;
                return uri[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase)
                    ? TryDecodeBase64(uri[(comma + 1)..], out var fromUri) ? (fromUri, null) : null
                    : (null, Uri.UnescapeDataString(uri[(comma + 1)..]));
            }

        // 3. content + encoding:"base64" marker.
        if (entry.TryGetProperty("encoding", out var enc) && enc.ValueKind == JsonValueKind.String
            && string.Equals(enc.GetString(), "base64", StringComparison.OrdinalIgnoreCase)
            && FirstString(entry, TextFields) is { } marked)
            return TryDecodeBase64(marked, out var fromMarked) ? (fromMarked, null) : null;

        // 4. Plain text content.
        if (FirstString(entry, TextFields) is { } text)
            return text.Length <= MaxBytes ? (null, text) : null;
        return null;
    }

    private static RunArtifact Build(string name, byte[]? bytes, string? text)
    {
        if (bytes is null) return new RunArtifact(name, text ?? "");
        // Same discriminator as /out capture: decoded base64 that is clean UTF-8 text stays text.
        return Utf8Text.TryDecode(bytes, out var decoded)
            ? new RunArtifact(name, decoded)
            : new RunArtifact(name, Convert.ToBase64String(bytes), isBinary: true);
    }

    private static bool TryDecodeBase64(string value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(value.Trim());
            return bytes.LongLength <= MaxBytes;
        }
        catch (FormatException)
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }

    private static string? FirstString(JsonElement entry, string[] fields)
    {
        foreach (var field in fields)
            if (entry.TryGetProperty(field, out var el) && el.ValueKind == JsonValueKind.String
                && el.GetString() is { Length: > 0 and <= MaxChars } s)
                return s;
        return null;
    }

    /// <summary>Relative, traversal-free artifact names only — anything else is dropped.</summary>
    private static string? SanitizeName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var name = raw.Replace('\\', '/').TrimStart('/').Trim();
        if (name.Length == 0 || name.Length > 260) return null;
        if (name.Split('/').Any(seg => seg is "" or "." or "..")) return null;
        return name;
    }
}
