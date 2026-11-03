using PlaceContext.Application.Access;
using PlaceContext.Application.Cqrs;
namespace PlaceContext.Application.Features;

public sealed record RevokeArtifactShareCommand(Guid ArtifactId)
    : ICommand<bool>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => Permission.ArtifactsShare;
}
