using PlaceContext.Application.Cqrs;
using PlaceContext.Operations.Contracts.Backup;

namespace PlaceContext.Operations.Backup;

/// <summary>
/// Restores/brings-up-to-date the current tenant from a <see cref="BackupManifest"/>. Projects are
/// resolved first (by path), building an old→new id map; jobs, chains, triggers, and data mappings are
/// then imported with their cross-references rewired through that map. A reference that can't be
/// resolved (unknown project/job/chain) is skipped and reported in the result, not thrown.
/// </summary>
public sealed record ImportManifestCommand(BackupManifest Manifest, ImportMode Mode = ImportMode.Merge)
    : ICommand<ImportResultView>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => PlaceContext.Application.Ports.Permission.BackupManage;
}
