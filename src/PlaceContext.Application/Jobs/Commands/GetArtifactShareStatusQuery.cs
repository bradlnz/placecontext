using PlaceContext.Application.Access;
using PlaceContext.Application.Artifacts;
using PlaceContext.Application.Cqrs;
namespace PlaceContext.Application.Features;

public sealed record GetArtifactShareStatusQuery(Guid ArtifactId)
    : IQuery<ArtifactShareStatus?>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => Permission.ArtifactsShare;
}
