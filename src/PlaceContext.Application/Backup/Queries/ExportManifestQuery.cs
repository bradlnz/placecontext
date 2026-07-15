using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Exports the current tenant's settings + job definitions to a portable <see cref="BackupManifest"/>.
/// Tenant-scoped implicitly — every repository read is filtered to the caller's tenant.</summary>
public sealed record ExportManifestQuery : IQuery<BackupManifest>;
