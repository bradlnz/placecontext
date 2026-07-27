using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Cluster;

/// <summary>
/// Designate <paramref name="NodeName"/> as the fleet master. Join codes and multi-site add-node
/// flows then target that node's Tailscale address. If the node is only a worker today, the result
/// includes the host-side k3s server reinstall steps (agent cannot become control-plane via the API alone).
/// </summary>
public sealed record PromoteNodeToMasterCommand(string NodeName) : ICommand<PromoteMasterResult>;

public sealed class PromoteNodeToMasterHandler : ICommandHandler<PromoteNodeToMasterCommand, PromoteMasterResult>
{
    private readonly IClusterAdminPort _admin;

    public PromoteNodeToMasterHandler(IClusterAdminPort admin) => _admin = admin;

    public Task<PromoteMasterResult> HandleAsync(PromoteNodeToMasterCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.NodeName))
            return Task.FromResult(new PromoteMasterResult("", false, "Node name is required."));
        return _admin.PromoteToMasterAsync(command.NodeName.Trim(), ct);
    }
}
