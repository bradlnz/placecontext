namespace PlaceContext.Sync.Transport;

/// <summary>
/// Raised when a peer violates the transport's security binding — e.g. its Hello claims a
/// <see cref="NodeId"/> that the TLS certificate it authenticated with does not certify.
/// </summary>
public sealed class SyncTransportException : Exception
{
    public SyncTransportException(string message) : base(message) { }
}
