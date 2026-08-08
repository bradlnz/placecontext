using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed record RemoveCrmClientArtifactCommand(Guid ArtifactId)
    : ICommand<bool>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
