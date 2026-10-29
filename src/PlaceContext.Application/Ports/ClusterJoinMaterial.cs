namespace PlaceContext.Application.Ports;

/// <summary>Material for adding a worker (or extra server) that connects via the mesh.</summary>
public sealed record ClusterJoinMaterial(
    string JoinCode,
    string ServerUrl,
    bool IncludesTailscaleAuthKey,
    string Instructions);
