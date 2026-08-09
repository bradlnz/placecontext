namespace PlaceContext.Application.Ports;

/// <summary>One span node in a captured job trace tree.</summary>
public sealed record TraceSpanNode(
    string Name,
    string? TraceId,
    string? SpanId,
    string? ParentSpanId,
    DateTimeOffset StartedAt,
    double DurationMs,
    IReadOnlyDictionary<string, string> Tags,
    IReadOnlyList<TraceSpanNode> Children);
