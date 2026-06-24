namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// A SHA-256 digest of the code a context note describes, used to detect staleness when the code
/// changes underneath the note. Value Object: 64 hex chars, lower-cased.
/// </summary>
public sealed record ContentDigest
{
    public string Sha256 { get; }

    private ContentDigest(string sha256) => Sha256 = sha256;

    public static ContentDigest From(string sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256))
            throw new ArgumentException("ContentDigest must not be empty.", nameof(sha256));

        var v = sha256.Trim().ToLowerInvariant();
        if (v.Length != 64 || !v.All(Uri.IsHexDigit))
            throw new ArgumentException("ContentDigest must be 64 hex chars (SHA-256).", nameof(sha256));

        return new ContentDigest(v);
    }

    public override string ToString() => Sha256;
}
