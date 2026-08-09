using PlaceContext.Application.Cqrs;
using PlaceContext.Operations.Contracts.Backup;

namespace PlaceContext.Operations.Backup;

internal sealed class BackupService(
    IQueryHandler<ExportManifestQuery, BackupManifest> exporter,
    ICommandHandler<ImportManifestCommand, ImportResultView> importer) : IBackupService
{
    public Task<BackupManifest> ExportManifestAsync(CancellationToken cancellationToken = default) =>
        exporter.HandleAsync(new ExportManifestQuery(), cancellationToken);

    public Task<ImportResultView> ImportManifestAsync(
        BackupManifest manifest,
        ImportMode mode = ImportMode.Merge,
        CancellationToken cancellationToken = default) =>
        importer.HandleAsync(new ImportManifestCommand(manifest, mode), cancellationToken);
}
