using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed record CreateArtifactShareCommand(Guid ArtifactId, int LifetimeDays = 7)
    : ICommand<ArtifactShareCreated>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => Permission.ArtifactsShare;
}
