namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// Structural metrics distilled from a project's graphify graph, consumed by the technical-risk
/// scorer. All ratios are in [0,1]; counts are non-negative.
/// </summary>
public readonly record struct GraphMetrics(
    int NodeCount,
    int LinkCount,
    int GodNodeCount,
    double AverageDegree,
    double LowConfidenceLinkRatio)
{
    public static GraphMetrics From(
        int nodeCount,
        int linkCount,
        int godNodeCount,
        double averageDegree,
        double lowConfidenceLinkRatio)
    {
        if (nodeCount < 0 || linkCount < 0 || godNodeCount < 0)
            throw new ArgumentException("GraphMetrics counts must be non-negative.");
        if (averageDegree < 0)
            throw new ArgumentException("AverageDegree must be non-negative.", nameof(averageDegree));

        return new GraphMetrics(
            nodeCount, linkCount, godNodeCount, averageDegree,
            Math.Clamp(lowConfidenceLinkRatio, 0.0, 1.0));
    }
}
