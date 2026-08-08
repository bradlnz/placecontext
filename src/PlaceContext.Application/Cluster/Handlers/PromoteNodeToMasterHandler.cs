using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Cluster;

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
