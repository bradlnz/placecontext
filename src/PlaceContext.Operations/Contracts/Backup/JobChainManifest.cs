using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Operations.Contracts.Backup;

/// <summary>An ordered job pipeline. Steps are old JobIds, rewired through the import's job map.
/// Natural key on import is (ProjectId, Name).</summary>
public sealed record JobChainManifest(
    Guid ChainId,
    Guid ProjectId,
    string Name,
    string? Description,
    IReadOnlyList<Guid> StepJobIds,
    IReadOnlyList<JobChainStageManifest>? Stages = null);
