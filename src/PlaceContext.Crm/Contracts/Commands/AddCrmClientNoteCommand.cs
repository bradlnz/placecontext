using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed record AddCrmClientNoteCommand(Guid ClientId, string Body)
    : ICommand<CrmCommunicationView>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
