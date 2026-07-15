using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

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
    public const int CurrentSchemaVersion = 1;
}

/// <summary>Tenant registry settings safe to carry across a backup (see <c>ITenantSettingsPort</c>).</summary>
public sealed record TenantSettingsManifest(string TimeZoneId, string? BrandingJson, string? GitHubLogin);

/// <summary>A project registry record. Natural key on import is <see cref="Path"/> (matches
/// <c>CreateProjectCommand</c>'s own idempotency rule).</summary>
public sealed record ProjectManifest(Guid ProjectId, string Name, string Path);

/// <summary>
/// A job definition. Mirrors <see cref="JobView"/>'s workload-source shape (image XOR runtime+files)
/// plus the fields backup fidelity needs that the UI read-model doesn't carry (<see cref="TimeoutSeconds"/>).
/// Natural key on import is (ProjectId, Name) within the resolved project.
/// </summary>
public sealed record JobManifest(
    Guid JobId,
    Guid ProjectId,
    string Name,
    string? Description,

    /// <summary>"image" or "code".</summary>
    string MapSourceKind,
    string? MapImage,
    string? MapRuntimeId,
    string? MapEntrypoint,
    IReadOnlyList<CodeFileDto> MapFiles,
    IReadOnlyList<string> InputPayloads,
    IReadOnlyDictionary<string, string> MapEnv,

    string? ReduceSourceKind,
    string? ReduceImage,
    string? ReduceRuntimeId,
    string? ReduceEntrypoint,
    IReadOnlyList<CodeFileDto> ReduceFiles,
    IReadOnlyDictionary<string, string>? ReduceEnv,

    int ConcurrencyLimit,
    IReadOnlyList<int> SuccessExitCodes,
    IReadOnlyList<int> PartialExitCodes,
    bool AllowNetworkEgress,
    int TimeoutSeconds,
    IReadOnlyList<JobParameterDto> Parameters,
    IReadOnlyList<PostJobActionKind> PostJobActions,
    JobReturnType ReturnType,
    string? ReturnFileName);

/// <summary>An ordered job pipeline. Steps are old JobIds, rewired through the import's job map.
/// Natural key on import is (ProjectId, Name).</summary>
public sealed record JobChainManifest(Guid ChainId, Guid ProjectId, string Name, string? Description, IReadOnlyList<Guid> StepJobIds);

/// <summary>A schedule/event trigger on a job. Natural key on import is (JobId, Name).</summary>
public sealed record TriggerManifest(
    Guid TriggerId, Guid ProjectId, Guid JobId, string Name,
    /// <summary>"Schedule" | "Event".</summary>
    string Kind,
    bool Enabled, string? CronExpression, string? EventName);

/// <summary>A user-defined event type. Natural key on import is <see cref="Name"/> (workspace-unique).</summary>
public sealed record EventDefinitionManifest(string Name, string? Description, string? PayloadSchema);

/// <summary>One edge of the data map. Natural key on import is (ProjectId, SourceKind, JobId, TargetTable).</summary>
public sealed record DataMappingManifest(
    Guid MappingId, Guid ProjectId, Guid JobId,
    /// <summary>"job" | "chain" — <see cref="JobId"/> is a chain id when this is "chain".</summary>
    string SourceKind,
    string TargetTable, string? RowsPath, IReadOnlyList<DataFieldDto> Fields, bool Enabled);
