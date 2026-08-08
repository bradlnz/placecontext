using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed record SendCrmClientMessageCommand(
    Guid ClientId,
    CrmCommunicationChannel Channel,
    string? Subject,
    string Body) : ICommand<CrmCommunicationView>, IRequiresPermission
{
    public string RequiredPermission => Permission.CrmCommsSend;
}
