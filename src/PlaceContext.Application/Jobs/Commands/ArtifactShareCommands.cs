using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed record CreateArtifactShareCommand(Guid ArtifactId, int LifetimeDays = 7)
    : ICommand<ArtifactShareCreated>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => Permission.ArtifactsShare;
}

public sealed record RevokeArtifactShareCommand(Guid ArtifactId)
    : ICommand<bool>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => Permission.ArtifactsShare;
}

public sealed record GetArtifactShareStatusQuery(Guid ArtifactId)
    : IQuery<ArtifactShareStatus?>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => Permission.ArtifactsShare;
}
