namespace PlaceContext.Domain.ValueObjects;

/// <summary>A graphify node identity inside a project's knowledge graph.</summary>
public readonly record struct GraphNodeId(string Value)
{
    public static GraphNodeId From(string value)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("GraphNodeId must not be empty.", nameof(value))
            : new GraphNodeId(value.Trim());

    public override string ToString() => Value;
}
