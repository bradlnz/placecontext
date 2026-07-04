using System.Security.Cryptography;

namespace PlaceContext.Sync;

/// <summary>
/// A self-certifying node identity: <c>base32-crockford(SHA-256(publicKey)[0..20])</c> — 160 bits,
/// 32 chars, no padding. Given a NodeId and a presented public key, either the key hashes to the id
/// or the peer is lying; there is no directory or CA to consult. Value Object.
/// </summary>
public sealed record NodeId
{
    // Crockford base32 alphabet (no I, L, O, U to avoid ambiguity).
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int RawBytes = 20; // 160 bits → 32 base32 chars

    public string Value { get; }

    private NodeId(string value) => Value = value;

    /// <summary>Derive a NodeId from a raw public key.</summary>
    public static NodeId FromPublicKey(ReadOnlySpan<byte> publicKey)
    {
        if (publicKey.IsEmpty)
            throw new ArgumentException("Public key must not be empty.", nameof(publicKey));

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(publicKey, hash);
        return new NodeId(Encode(hash[..RawBytes]));
    }

    /// <summary>Parse an existing NodeId string (32 Crockford base32 chars, case-insensitive).</summary>
    public static NodeId Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("NodeId must not be empty.", nameof(value));

        var v = value.Trim().ToUpperInvariant();
        if (v.Length != 32 || !v.All(c => Alphabet.IndexOf(c) >= 0))
            throw new ArgumentException("NodeId must be 32 Crockford base32 chars.", nameof(value));

        return new NodeId(v);
    }

    /// <summary>True if <paramref name="publicKey"/> hashes to this id — the self-certification check.</summary>
    public bool Certifies(ReadOnlySpan<byte> publicKey) => FromPublicKey(publicKey) == this;

    private static string Encode(ReadOnlySpan<byte> data)
    {
        // Big-endian bit accumulator, 5 bits per output char.
        var chars = new char[(data.Length * 8 + 4) / 5];
        int bitBuffer = 0, bits = 0, o = 0;
        foreach (var b in data)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                chars[o++] = Alphabet[(bitBuffer >> bits) & 0x1F];
            }
        }
        if (bits > 0)
            chars[o++] = Alphabet[(bitBuffer << (5 - bits)) & 0x1F];
        return new string(chars, 0, o);
    }

    public override string ToString() => Value;
}
