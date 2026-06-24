namespace PlaceContext.Domain.Common;

/// <summary>
/// Base for aggregate roots. Records domain events raised by behavior; the Application drains them
/// with <see cref="PullDomainEvents"/> after a successful unit of work and dispatches them.
/// </summary>
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = new();

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>Returns and clears the pending domain events.</summary>
    public IReadOnlyList<IDomainEvent> PullDomainEvents()
    {
        var drained = _domainEvents.ToArray();
        _domainEvents.Clear();
        return drained;
    }
}
