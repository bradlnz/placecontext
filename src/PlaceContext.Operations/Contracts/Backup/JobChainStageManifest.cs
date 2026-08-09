using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Operations.Contracts.Backup;

/// <summary>Version-2 staged chain shape. Null preserves version-1 flat-manifest compatibility.</summary>
public sealed record JobChainStageManifest(
    IReadOnlyList<Guid> JobIds,
    ChainGateManifest? Gate = null,
    ChainActionManifest? Action = null);
