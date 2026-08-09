using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Operations.Contracts.Backup;

/// <summary>A user-defined event type. Natural key on import is <see cref="Name"/> (workspace-unique).</summary>
public sealed record EventDefinitionManifest(string Name, string? Description, string? PayloadSchema);
