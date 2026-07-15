namespace PlaceContext.Application.Dtos;

/// <summary>Which entities a natural-key match creates vs. updates. Merge is the only mode today:
/// create what's missing, update what matches, never duplicate. Reserved for future modes (e.g. a
/// clean "replace" that first archives anything not in the manifest).</summary>
public enum ImportMode
{
    Merge,
}

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
