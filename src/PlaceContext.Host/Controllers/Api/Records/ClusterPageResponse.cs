namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record ClusterPageResponse(
    bool IsRealCluster,
    string? DesignatedMasterName,
    IReadOnlyList<ClusterNodeResponse> Nodes,
    string LastSyncLabel);
