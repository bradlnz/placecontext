using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed record GetArtifactShareStatusQuery(Guid ArtifactId)
    : IQuery<ArtifactShareStatus?>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => Permission.ArtifactsShare;
}
