namespace PlaceContext.Operations.Contracts.Backup;

/// <summary>Exports and restores portable tenant workspace configuration.</summary>
public interface IBackupService
{
    Task<BackupManifest> ExportManifestAsync(CancellationToken cancellationToken = default);

    Task<ImportResultView> ImportManifestAsync(
        BackupManifest manifest,
        ImportMode mode = ImportMode.Merge,
        CancellationToken cancellationToken = default);
}
