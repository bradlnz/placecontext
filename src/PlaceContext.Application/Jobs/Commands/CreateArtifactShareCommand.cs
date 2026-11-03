using PlaceContext.Application.Access;
using PlaceContext.Application.Artifacts;
using PlaceContext.Application.Cqrs;
namespace PlaceContext.Application.Features;

public sealed record CreateArtifactShareCommand(Guid ArtifactId, int LifetimeDays = 7)
    : ICommand<ArtifactShareCreated>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => Permission.ArtifactsShare;
}
