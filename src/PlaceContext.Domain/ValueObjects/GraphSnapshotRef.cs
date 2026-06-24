namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// Lightweight reference to the last graphify build for a project: where the graph lives, when it
/// was built, and the headline counts. The full graph stays on disk; this is the registry record.
/// </summary>
public sealed record GraphSnapshotRef
{
    public string GraphJsonPath { get; }
    public DateTimeOffset BuiltAt { get; }
    public int NodeCount { get; }
    public int LinkCount { get; }
    public IReadOnlyList<GodNode> GodNodes { get; }

    private GraphSnapshotRef(
        string graphJsonPath,
        DateTimeOffset builtAt,
        int nodeCount,
        int linkCount,
        IReadOnlyList<GodNode> godNodes)
    {
        GraphJsonPath = graphJsonPath;
        BuiltAt = builtAt;
        NodeCount = nodeCount;
        LinkCount = linkCount;
        GodNodes = godNodes;
    }

    public static GraphSnapshotRef Of(
        string graphJsonPath,
        DateTimeOffset builtAt,
        int nodeCount,
        int linkCount,
        IEnumerable<GodNode> godNodes)
    {
        if (string.IsNullOrWhiteSpace(graphJsonPath))
            throw new ArgumentException("GraphJsonPath must not be empty.", nameof(graphJsonPath));
        if (nodeCount < 0 || linkCount < 0)
            throw new ArgumentException("Graph counts must be non-negative.");

        return new GraphSnapshotRef(graphJsonPath, builtAt, nodeCount, linkCount, godNodes.ToList());
    }
}
