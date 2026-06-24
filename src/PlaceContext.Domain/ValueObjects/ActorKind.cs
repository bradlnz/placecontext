namespace PlaceContext.Domain.ValueObjects;

/// <summary>Who (or what) authored a change. The distinction drives agentic-debt scoring.</summary>
public enum ActorKind
{
    Human,
    Agent
}
