namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// A highly-connected ("god") node — a hotspot of coupling. Touching one is an process-risk
/// signal and a technical-risk contributor.
/// </summary>
public sealed record GodNode
{
    public GraphNodeId Id { get; }
    public NormLabel Label { get; }
    public int Degree { get; }

    private GodNode(GraphNodeId id, NormLabel label, int degree)
    {
        Id = id;
        Label = label;
        Degree = degree;
    }

    public static GodNode Of(GraphNodeId id, NormLabel label, int degree)
    {
        if (degree < 0)
            throw new ArgumentException("GodNode degree must be non-negative.", nameof(degree));

        return new GodNode(id, label, degree);
    }

    public override string ToString() => $"{Label} (deg {Degree})";
}
