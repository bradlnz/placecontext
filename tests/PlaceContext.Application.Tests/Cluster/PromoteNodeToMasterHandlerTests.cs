using PlaceContext.Application.Cluster;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Tests.Cluster;

public sealed class PromoteNodeToMasterHandlerTests
{
    [Fact]
    public async Task Empty_node_name_fails()
    {
        var admin = new FakeAdmin();
        var h = new PromoteNodeToMasterHandler(admin);
        var r = await h.HandleAsync(new PromoteNodeToMasterCommand("  "), default);
        Assert.False(r.Succeeded);
        Assert.Empty(admin.Promoted);
    }

    [Fact]
    public async Task Delegates_to_admin_port()
    {
        var admin = new FakeAdmin();
        var h = new PromoteNodeToMasterHandler(admin);
        var r = await h.HandleAsync(new PromoteNodeToMasterCommand(" mac-node "), default);
        Assert.True(r.Succeeded);
        Assert.Equal(new[] { "mac-node" }, admin.Promoted);
        Assert.Equal("mac-node", r.NodeName);
    }

    private sealed class FakeAdmin : IClusterAdminPort
    {
        public List<string> Promoted { get; } = new();

        public Task<PromoteMasterResult> PromoteToMasterAsync(string nodeName, CancellationToken ct = default)
        {
            Promoted.Add(nodeName);
            return Task.FromResult(new PromoteMasterResult(nodeName, true, "ok"));
        }

        public Task<ClusterJoinMaterial?> GetJoinMaterialAsync(CancellationToken ct = default)
            => Task.FromResult<ClusterJoinMaterial?>(null);

        public Task<ClusterJoinMaterial?> GetJoinMaterialAsync(string? tailscaleAuthKeyOverride, CancellationToken ct = default)
            => Task.FromResult<ClusterJoinMaterial?>(null);
    }
}
