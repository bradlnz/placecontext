using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>Permanently removes many artifacts at once (bulk selection). Returns the number actually deleted.</summary>
public sealed record DeleteArtifactsCommand(IReadOnlyList<Guid> ArtifactIds) : ICommand<int>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => Ports.Permission.ArtifactsDelete;
}
