using PlaceContext.Infrastructure.Connections;

namespace PlaceContext.Infrastructure.Tests.Connections;

public sealed class ManagedOAuthProtocolTests
{
    [Fact]
    public void Pkce_challenge_matches_RFC_7636_S256_test_vector()
    {
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            ManagedOAuthProtocol.Challenge(verifier));
    }

    [Fact]
    public void OAuth_state_is_random_and_has_stable_irreversible_sha256_digest()
    {
        var first = ManagedOAuthProtocol.RandomState();
        var second = ManagedOAuthProtocol.RandomState();
        Assert.NotEqual(first, second);
        Assert.True(first.Length >= 43);
        Assert.Equal(ManagedOAuthProtocol.HashState(first), ManagedOAuthProtocol.HashState(first));
        Assert.NotEqual(ManagedOAuthProtocol.HashState(first), ManagedOAuthProtocol.HashState(second));
        Assert.DoesNotContain(first, ManagedOAuthProtocol.HashState(first));
    }

    [Fact]
    public void Invalid_pkce_verifiers_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => ManagedOAuthProtocol.Challenge("short"));
        Assert.Throws<ArgumentException>(() => ManagedOAuthProtocol.Challenge(new string('x', 129)));
    }
}
