using PlaceContext.Application.Cluster;
using PlaceContext.Application.Ports;
using PlaceContext.Vault.Domain.Repositories;

namespace PlaceContext.Application.Tests.Cluster;

public sealed class LaunchClusterAgentHandlerTests
{
    [Fact]
    public async Task Missing_credentials_reports_minted_false()
    {
        var secrets = new FakeSecrets();
        var minter = new FakeMinter { Key = "tskey-auth-should-not-be-used" };
        var admin = new FakeAdmin();
        var h = new LaunchClusterAgentHandler(secrets, new FakeProtector(), minter, admin);

        var r = await h.HandleAsync(new LaunchClusterAgentCommand(), default);

        Assert.False(r.Minted);
        Assert.Null(r.JoinCode);
        Assert.Contains("TS_CLIENT_ID", r.Message);
        Assert.Contains("TS_CLIENT_SECRET", r.Message);
        Assert.Empty(minter.Calls);
    }

    [Fact]
    public async Task Minter_failure_reports_minted_false()
    {
        var secrets = new FakeSecrets();
        secrets.Seed(LaunchClusterAgentHandler.ClientIdSecretName, "id-1");
        secrets.Seed(LaunchClusterAgentHandler.ClientSecretSecretName, "secret-1");
        var minter = new FakeMinter { Key = null };
        var admin = new FakeAdmin();
        var h = new LaunchClusterAgentHandler(secrets, new FakeProtector(), minter, admin);

        var r = await h.HandleAsync(new LaunchClusterAgentCommand(), default);

        Assert.False(r.Minted);
        Assert.Null(r.JoinCode);
        Assert.Single(minter.Calls);
    }

    [Fact]
    public async Task Happy_path_mints_key_and_returns_join_code()
    {
        var secrets = new FakeSecrets();
        secrets.Seed(LaunchClusterAgentHandler.ClientIdSecretName, "id-1");
        secrets.Seed(LaunchClusterAgentHandler.ClientSecretSecretName, "secret-1");
        secrets.Seed(LaunchClusterAgentHandler.TagSecretName, "tag:custom");
        var minter = new FakeMinter { Key = "tskey-auth-abc123" };
        var admin = new FakeAdmin
        {
            JoinResult = new ClusterJoinMaterial("PC2.abc", "https://master:6443", true, "instructions"),
        };
        var h = new LaunchClusterAgentHandler(secrets, new FakeProtector(), minter, admin);

        var r = await h.HandleAsync(new LaunchClusterAgentCommand(), default);

        Assert.True(r.Minted);
        Assert.Equal("PC2.abc", r.JoinCode);
        Assert.Equal("https://master:6443", r.ServerUrl);
        Assert.Contains("PC2.abc", r.ConnectCommand);
        Assert.Single(minter.Calls);
        Assert.Equal(("id-1", "secret-1", "tag:custom"), minter.Calls[0]);
        Assert.Equal("tskey-auth-abc123", admin.LastOverrideKey);
    }

    [Fact]
    public async Task Default_tag_used_when_TS_TAG_not_set()
    {
        var secrets = new FakeSecrets();
        secrets.Seed(LaunchClusterAgentHandler.ClientIdSecretName, "id-1");
        secrets.Seed(LaunchClusterAgentHandler.ClientSecretSecretName, "secret-1");
        var minter = new FakeMinter { Key = "tskey-auth-abc123" };
        var admin = new FakeAdmin
        {
            JoinResult = new ClusterJoinMaterial("PC2.abc", "https://master:6443", true, "instructions"),
        };
        var h = new LaunchClusterAgentHandler(secrets, new FakeProtector(), minter, admin);

        await h.HandleAsync(new LaunchClusterAgentCommand(), default);

        Assert.Equal("tag:agent", minter.Calls[0].Tags);
    }

    [Fact]
    public async Task Join_secret_not_seeded_reports_minted_true_but_no_code()
    {
        var secrets = new FakeSecrets();
        secrets.Seed(LaunchClusterAgentHandler.ClientIdSecretName, "id-1");
        secrets.Seed(LaunchClusterAgentHandler.ClientSecretSecretName, "secret-1");
        var minter = new FakeMinter { Key = "tskey-auth-abc123" };
        var admin = new FakeAdmin { JoinResult = null };
        var h = new LaunchClusterAgentHandler(secrets, new FakeProtector(), minter, admin);

        var r = await h.HandleAsync(new LaunchClusterAgentCommand(), default);

        Assert.True(r.Minted);
        Assert.Null(r.JoinCode);
    }

    // ── fakes ─────────────────────────────────────────────────────────────────────────────────────

    private sealed class FakeSecrets : IProjectSecretRepository
    {
        private readonly Dictionary<string, string> _ciphers = new();

        public void Seed(string name, string plaintext) => _ciphers[name] = FakeProtector.Encode(plaintext);

        public Task<IReadOnlyList<(string Name, DateTimeOffset CreatedAt)>> ListAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<(string, DateTimeOffset)>>(Array.Empty<(string, DateTimeOffset)>());

        public Task AddAsync(Guid projectId, string name, string cipher, DateTimeOffset now, CancellationToken ct = default)
        {
            _ciphers[name] = cipher;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid projectId, string name, CancellationToken ct = default)
        {
            _ciphers.Remove(name);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string>> GetCiphersAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(_ciphers));
    }

    /// <summary>Reversible fake — "protects" by prefixing so tests can assert round-tripping without real crypto.</summary>
    private sealed class FakeProtector : ISecretProtector
    {
        private const string Prefix = "enc:";

        public static string Encode(string plaintext) => Prefix + plaintext;

        public string Protect(string plaintext) => Encode(plaintext);

        public string Unprotect(string ciphertext) => ciphertext.StartsWith(Prefix, StringComparison.Ordinal)
            ? ciphertext[Prefix.Length..]
            : ciphertext;
    }

    private sealed class FakeMinter : ITailscaleKeyMinter
    {
        public string? Key { get; set; }
        public List<(string ClientId, string ClientSecret, string Tags)> Calls { get; } = new();

        public Task<string?> MintEphemeralAgentKeyAsync(string clientId, string clientSecret, string tags, CancellationToken ct = default)
        {
            Calls.Add((clientId, clientSecret, tags));
            return Task.FromResult(Key);
        }
    }

    private sealed class FakeAdmin : IClusterAdminPort
    {
        public ClusterJoinMaterial? JoinResult { get; set; }
        public string? LastOverrideKey { get; private set; }

        public Task<PromoteMasterResult> PromoteToMasterAsync(string nodeName, CancellationToken ct = default)
            => Task.FromResult(new PromoteMasterResult(nodeName, true, "ok"));

        public Task<ClusterJoinMaterial?> GetJoinMaterialAsync(CancellationToken ct = default)
            => GetJoinMaterialAsync(null, ct);

        public Task<ClusterJoinMaterial?> GetJoinMaterialAsync(string? tailscaleAuthKeyOverride, CancellationToken ct = default)
        {
            LastOverrideKey = tailscaleAuthKeyOverride;
            return Task.FromResult(JoinResult);
        }
    }
}
