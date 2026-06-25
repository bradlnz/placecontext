namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// Code-level metrics probed from the working tree, consumed by the technical-risk scorer.
/// <see cref="CoverageGap"/> is 1 − coverage in [0,1]; pass −1 coverage as gap 0 when unknown.
/// </summary>
public readonly record struct CodeMetrics(
    int TodoFixmeCount,
    int HighComplexityCount,
    double CoverageGap,
    int FileCount)
{
    public static CodeMetrics From(
        int todoFixmeCount,
        int highComplexityCount,
        double coverageGap,
        int fileCount)
    {
        if (todoFixmeCount < 0 || highComplexityCount < 0 || fileCount < 0)
            throw new ArgumentException("CodeMetrics counts must be non-negative.");

        return new CodeMetrics(
            todoFixmeCount, highComplexityCount,
            Math.Clamp(coverageGap, 0.0, 1.0), fileCount);
    }

    /// <summary>TODO/FIXME markers per file — normalized hotspot density.</summary>
    public double TodoDensity => FileCount == 0 ? 0.0 : (double)TodoFixmeCount / FileCount;
}
