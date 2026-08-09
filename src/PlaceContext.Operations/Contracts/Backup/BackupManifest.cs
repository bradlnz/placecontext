using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Operations.Contracts.Backup;

/// <summary>
/// Versioned, portable snapshot of a tenant workspace's configuration: projects, job definitions,
/// chains, triggers, event types, data mappings, and tenant settings. Round-trips through
/// <c>ExportManifestQuery</c> / <c>ImportManifestCommand</c>. Deliberately excludes run history
/// (JobRun/ChainRun/Usage/Activity/pending runs — operational data, not configuration) and vault
/// secrets (Data-Protection-encrypted ciphertext is bound to this deployment's key ring and cannot be
/// decrypted anywhere else) — see <see cref="Notes"/>.
/// </summary>
public sealed record BackupManifest(
    int SchemaVersion,
    DateTimeOffset ExportedAt,
    TenantSettingsManifest TenantSettings,
    IReadOnlyList<ProjectManifest> Projects,
    IReadOnlyList<JobManifest> Jobs,
    IReadOnlyList<JobChainManifest> JobChains,
    IReadOnlyList<TriggerManifest> Triggers,
    IReadOnlyList<EventDefinitionManifest> EventDefinitions,
    IReadOnlyList<DataMappingManifest> DataMappings,
    string Notes =
        "Excludes run history (JobRun/ChainRun/Usage/Activity/pending runs) and vault secrets " +
        "(Data-Protection-encrypted ciphertext, non-portable across deployments).")
{
    /// <summary>Schema version this build writes and can read. Bump when a manifest shape changes.</summary>
    public const int CurrentSchemaVersion = 2;
}
