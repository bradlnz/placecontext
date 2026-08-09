using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Operations.Contracts.Backup;

/// <summary>A project registry record. Natural key on import is <see cref="Path"/> (matches
/// <c>CreateProjectCommand</c>'s own idempotency rule).</summary>
public sealed record ProjectManifest(Guid ProjectId, string Name, string Path);
