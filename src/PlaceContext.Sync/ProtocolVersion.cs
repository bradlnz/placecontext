namespace PlaceContext.Sync;

/// <summary>
/// PCSP wire version as <c>major.minor</c>. Peers adopt the lowest common version; a mismatched
/// <see cref="Major"/> is incompatible (see <see cref="TryNegotiate"/>). Value Object.
/// </summary>
public sealed record ProtocolVersion(int Major, int Minor) : IComparable<ProtocolVersion>
{
    /// <summary>The version this build speaks. 1.1 added the Chat message (kind 5).</summary>
    public static readonly ProtocolVersion Current = new(1, 1);

    public int CompareTo(ProtocolVersion? other)
    {
        if (other is null) return 1;
        var m = Major.CompareTo(other.Major);
        return m != 0 ? m : Minor.CompareTo(other.Minor);
    }

    /// <summary>
    /// Negotiate the version to use with a peer: same <see cref="Major"/> required, then the lower
    /// <see cref="Minor"/> (unknown newer minor features are simply not used). Returns false when
    /// the majors differ — the connection is incompatible and should be closed with a Bye.
    /// </summary>
    public bool TryNegotiate(ProtocolVersion peer, out ProtocolVersion agreed)
    {
        if (Major != peer.Major)
        {
            agreed = this;
            return false;
        }
        agreed = new ProtocolVersion(Major, Math.Min(Minor, peer.Minor));
        return true;
    }

    public override string ToString() => $"{Major}.{Minor}";
}
