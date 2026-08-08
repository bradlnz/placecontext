using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed record AttachCrmClientArtifactCommand(
    Guid ClientId,
    string FileName,
    string? ContentType,
    byte[] Content) : ICommand<CrmClientArtifactView>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
