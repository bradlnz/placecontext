using PlaceContext.Sync;
using PlaceContext.Sync.Transport;

namespace PlaceContext.Sync.Transport.Tests;

public class SyncTlsIdentityTests
{
    [Fact]
    public void NodeId_is_derived_from_the_certificate_public_key()
    {
        using var identity = SyncTlsIdentity.Create();

        // The self-certification check: the cert's public key must hash to the identity's NodeId.
        Assert.Equal(identity.NodeId, SyncTlsIdentity.NodeIdFromCertificate(identity.Certificate));
        Assert.True(identity.NodeId.Certifies(identity.Certificate.PublicKey.ExportSubjectPublicKeyInfo()));
    }

    [Fact]
    public void Certificate_carries_a_private_key_for_tls()
    {
        using var identity = SyncTlsIdentity.Create();
        Assert.True(identity.Certificate.HasPrivateKey);
    }

    [Fact]
    public void Key_pem_roundtrip_preserves_the_node_identity()
    {
        using var original = SyncTlsIdentity.Create();
        using var restored = SyncTlsIdentity.FromKeyPem(original.ExportKeyPem());

        Assert.Equal(original.NodeId, restored.NodeId);
    }

    [Fact]
    public void Distinct_identities_get_distinct_node_ids()
    {
        using var a = SyncTlsIdentity.Create();
        using var b = SyncTlsIdentity.Create();

        Assert.NotEqual(a.NodeId, b.NodeId);
    }
}
