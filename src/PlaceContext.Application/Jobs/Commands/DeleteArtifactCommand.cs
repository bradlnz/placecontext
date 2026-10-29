using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>Permanently removes a stored artifact — both the link row and the object bytes. Returns true if it existed.</summary>
public sealed record DeleteArtifactCommand(Guid ArtifactId) : ICommand<bool>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => PlaceContext.Application.Ports.Permission.ArtifactsDelete;
}
