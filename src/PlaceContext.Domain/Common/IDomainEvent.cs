namespace PlaceContext.Domain.Common;

/// <summary>Marker for a past-tense fact raised by the Domain and dispatched by the Application.</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
