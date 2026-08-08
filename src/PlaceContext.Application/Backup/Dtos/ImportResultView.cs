namespace PlaceContext.Application.Dtos;

/// <summary>Summary of what an <c>ImportManifestCommand</c> did — one create/update/skip count per
/// entity kind, plus human-readable warnings for anything skipped (dangling references, invalid data).</summary>
public sealed record ImportResultView(
    int ProjectsCreated, int ProjectsUpdated,
    int JobsCreated, int JobsUpdated, int JobsSkipped,
    int JobChainsCreated, int JobChainsUpdated, int JobChainsSkipped,
    int TriggersCreated, int TriggersUpdated, int TriggersSkipped,
    int EventDefinitionsCreated, int EventDefinitionsUpdated, int EventDefinitionsSkipped,
    int DataMappingsCreated, int DataMappingsUpdated, int DataMappingsSkipped,
    bool TenantSettingsApplied,
    IReadOnlyList<string> Warnings);
