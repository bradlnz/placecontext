using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Ports;

/// <summary>Emits user and domain events without coupling shared application workflows to Jobs.</summary>
public interface IEventDispatcher
{
    Task<EventOccurrenceView> EmitAsync(
        string name,
        Guid? projectId,
        string? payload,
        CancellationToken cancellationToken = default);

    Task<EventOccurrenceView> RaiseAsync(
        string name,
        Guid? projectId,
        string? payload,
        CancellationToken cancellationToken = default);
}
