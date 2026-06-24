namespace PlaceContext.Domain.ValueObjects;

/// <summary>The author of a change — a named human or a named agent.</summary>
public sealed record Author
{
    public string Name { get; }
    public ActorKind Kind { get; }

    private Author(string name, ActorKind kind)
    {
        Name = name;
        Kind = kind;
    }

    public bool IsAgent => Kind == ActorKind.Agent;

    public static Author From(string name, ActorKind kind)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Author name must not be empty.", nameof(name));

        return new Author(name.Trim(), kind);
    }

    public static Author Human(string name) => From(name, ActorKind.Human);
    public static Author Agent(string name) => From(name, ActorKind.Agent);

    public override string ToString() => $"{Name} ({Kind})";
}
