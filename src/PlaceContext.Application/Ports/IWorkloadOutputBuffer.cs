namespace PlaceContext.Application.Ports;

/// <summary>A bounded, short-lived view of stdout/stderr for a workload that is still executing.</summary>
public sealed record LiveWorkloadOutput(
    string Text,
    bool IsComplete,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Bridges infrastructure process/pod logs back to the interactive portal. Correlation ids start
/// with the owning job-run id, so a chain step can collect all map/reduce streams by run id.
/// Implementations must be thread-safe; output is operational and intentionally short-lived.
/// </summary>
public interface IWorkloadOutputBuffer
{
    void Append(string correlationId, string text, bool isError = false);
    void Set(string correlationId, string stdout, string stderr = "");
    void Complete(string correlationId);
    LiveWorkloadOutput? Snapshot(Guid runId);
}
