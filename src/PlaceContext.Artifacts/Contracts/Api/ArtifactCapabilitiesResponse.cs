namespace PlaceContext.Artifacts.Contracts.Api;

public sealed record ArtifactCapabilitiesResponse(
    bool CanDelete,
    bool CanShare,
    bool CanManageSettings);
