using PlaceContext.Application.Cluster;
using PlaceContext.Application.Ports;
using PlaceContext.Agents.Cluster;

namespace PlaceContext.Application.Tests.Cluster;

public sealed class LaunchClusterAgentHandlerTests
{
    [Fact]
    public async Task Missing_credentials_reports_minted_false()
    {
        var secrets = new FakeSecrets();
        var minter = new FakeMinter { Key = "tskey-auth-should-not-be-used" };
        var admin = new FakeAdmin();
        var h = new LaunchClusterAgentHandler(secrets, minter, admin);

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
        var h = new LaunchClusterAgentHandler(secrets, minter, admin);

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
        var h = new LaunchClusterAgentHandler(secrets, minter, admin);

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
        var h = new LaunchClusterAgentHandler(secrets, minter, admin);

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
        var h = new LaunchClusterAgentHandler(secrets, minter, admin);

        var r = await h.HandleAsync(new LaunchClusterAgentCommand(), default);

        Assert.True(r.Minted);
        Assert.Null(r.JoinCode);
    }

    // ── fakes ─────────────────────────────────────────────────────────────────────────────────────

    private sealed class FakeSecrets : IAgentSecretProvider
    {
        private readonly Dictionary<string, string> _values = new();

        public void Seed(string name, string plaintext) => _values[name] = plaintext;

        public Task<IReadOnlyDictionary<string, string>> GetSecretsAsync(
            Guid projectId, IReadOnlyList<string> names, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(names
                .Where(_values.ContainsKey)
                .ToDictionary(name => name, name => _values[name]));
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
