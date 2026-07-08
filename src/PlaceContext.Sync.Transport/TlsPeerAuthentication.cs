using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace PlaceContext.Sync.Transport;

/// <summary>
/// The one place the TLS posture is defined, shared by the sync and chat endpoints: TLS 1.3 only,
/// both sides always present a certificate, and trust is the self-certifying NodeId check — never a
/// chain, a CA, or an expiry date.
/// </summary>
internal static class TlsPeerAuthentication
{
    internal static SslServerAuthenticationOptions ServerOptions(SyncTlsIdentity identity) => new()
    {
        ServerCertificate = identity.Certificate,
        ClientCertificateRequired = true,
        EnabledSslProtocols = SslProtocols.Tls13,
        // Self-signed by design: the certificate must exist; which node it certifies is checked
        // against the expected/claimed identity by the caller.
        RemoteCertificateValidationCallback = static (_, certificate, _, _) => certificate is not null,
    };

    internal static SslClientAuthenticationOptions ClientOptions(SyncTlsIdentity identity, NodeId expectedPeer) => new()
    {
        TargetHost = expectedPeer.Value,
        EnabledSslProtocols = SslProtocols.Tls13,
        ClientCertificates = new X509CertificateCollection { identity.Certificate },
        // Identity pinning replaces chain trust: the presented key must hash to the peer we set
        // out to reach. Anything else — no cert, or someone else's — is refused.
        RemoteCertificateValidationCallback = (_, certificate, _, _) =>
            certificate is not null
            && SyncTlsIdentity.NodeIdFromCertificate(certificate) == expectedPeer,
    };
}
