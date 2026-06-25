namespace PlaceContext.Domain.ValueObjects;

/// <summary>Who (or what) authored a change. The distinction drives process-risk scoring.</summary>
public enum ActorKind
{
    Human,
    Agent
}
