namespace PlaceContext.Agents.Contracts.Api;

public sealed record ClusterPageResponse(
    bool IsRealCluster,
    string? DesignatedMasterName,
    IReadOnlyList<ClusterNodeResponse> Nodes,
    string LastSyncLabel);
