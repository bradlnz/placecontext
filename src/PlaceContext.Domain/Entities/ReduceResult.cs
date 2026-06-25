namespace PlaceContext.Domain.Entities;

/// <summary>
/// The outcome of the optional reduce container step. Owned by its <see cref="JobRun"/>.
/// The artifact is opaque — PlaceContext does not interpret its content.
/// </summary>
public sealed class ReduceResult
{
    public ReduceResult(int exitCode, bool succeeded, string? artifact, string? log)
    {
        ExitCode = exitCode;
        Succeeded = succeeded;
        Artifact = artifact;
        Log = log;
    }

    /// <summary>Raw exit code returned by the reduce container.</summary>
    public int ExitCode { get; }

    /// <summary>Whether the reduce step was considered successful (exit code in policy's success set).</summary>
    public bool Succeeded { get; }

    /// <summary>Opaque JSON artifact written by the reduce container to /out/result.json.</summary>
    public string? Artifact { get; }

    /// <summary>Combined stdout/stderr from the reduce container. May be truncated.</summary>
    public string? Log { get; }
}
