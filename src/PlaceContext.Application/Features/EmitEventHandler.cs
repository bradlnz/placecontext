using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

public sealed class EmitEventHandler : ICommandHandler<EmitEventCommand, EventOccurrenceView>
{
    private readonly EventDispatchService _dispatch;

    public EmitEventHandler(EventDispatchService dispatch) => _dispatch = dispatch;

    public Task<EventOccurrenceView> HandleAsync(EmitEventCommand command, CancellationToken ct = default)
        => _dispatch.EmitAsync(command.Name, command.ProjectId, command.Payload, ct);
}
