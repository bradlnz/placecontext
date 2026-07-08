using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PlaceContext.Sync.Transport;

/// <summary>
/// A node's transport identity: a long-lived ECDSA P-256 keypair wrapped in a self-signed X.509
/// certificate. The <see cref="NodeId"/> is derived from the key's SubjectPublicKeyInfo, so the TLS
/// certificate a peer presents *is* its identity claim — verify the key hashes to the expected id
/// and authentication is done, with no CA and no directory (docs/SYNC-PROTOCOL.md §2, §8).
/// </summary>
public sealed class SyncTlsIdentity : IDisposable
{
    private readonly ECDsa _key;

    public NodeId NodeId { get; }

    /// <summary>Self-signed certificate (with private key) presented on both ends of the TLS handshake.</summary>
    public X509Certificate2 Certificate { get; }

    private SyncTlsIdentity(ECDsa key)
    {
        _key = key;
        NodeId = NodeId.FromPublicKey(key.ExportSubjectPublicKeyInfo());
        Certificate = BuildCertificate(key, NodeId);
    }

    /// <summary>Generate a fresh identity (new keypair + certificate).</summary>
    public static SyncTlsIdentity Create() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    /// <summary>Restore an identity from a previously exported private-key PEM.</summary>
    public static SyncTlsIdentity FromKeyPem(string pem)
    {
        var key = ECDsa.Create();
        try
        {
            key.ImportFromPem(pem);
            return new SyncTlsIdentity(key);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <summary>The private key as a PKCS#8 PEM — persist this to keep a stable NodeId across restarts.</summary>
    public string ExportKeyPem() => _key.ExportPkcs8PrivateKeyPem();

    /// <summary>Derive the NodeId a certificate certifies — the self-certification check.</summary>
    public static NodeId NodeIdFromCertificate(X509Certificate2 certificate) =>
        NodeId.FromPublicKey(certificate.PublicKey.ExportSubjectPublicKeyInfo());

    internal static NodeId NodeIdFromCertificate(X509Certificate certificate) =>
        NodeIdFromCertificate(certificate as X509Certificate2
            ?? X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert)));

    private static X509Certificate2 BuildCertificate(ECDsa key, NodeId id)
    {
        var request = new CertificateRequest($"CN={id.Value}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection
            {
                new Oid("1.3.6.1.5.5.7.3.1"), // serverAuth — a PCSP node listens…
                new Oid("1.3.6.1.5.5.7.3.2"), // clientAuth — …and dials; same identity both ways
            },
            critical: false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(id.Value);
        request.CertificateExtensions.Add(san.Build());

        // Long validity: this is a self-certifying identity, not PKI — trust comes from the NodeId
        // hash check, never from expiry or a chain.
        var now = DateTimeOffset.UtcNow;
        using var ephemeral = request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(20));

        // PKCS#12 round-trip so SslStream can use the private key on every platform.
        return X509CertificateLoader.LoadPkcs12(
            ephemeral.Export(X509ContentType.Pkcs12), null, X509KeyStorageFlags.Exportable);
    }

    public void Dispose()
    {
        Certificate.Dispose();
        _key.Dispose();
    }
}
