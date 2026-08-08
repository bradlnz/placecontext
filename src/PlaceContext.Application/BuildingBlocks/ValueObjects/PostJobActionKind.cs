namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// A post-job action: run once a job finishes, it extracts the run's artifacts and produces an output
/// stored in the object store (surfaced as a link in the portal/TUI). Generic — no domain knowledge.
/// </summary>
public enum PostJobActionKind
{
    /// <summary>A self-contained styled HTML report summarizing the run + its artifacts.</summary>
    HtmlReport,

    /// <summary>An HTML page with an offline inline-SVG chart of the run (shard outcomes / numeric fields).</summary>
    Chart,

    /// <summary>A CSV flattening the run's artifacts.</summary>
    Csv,

    /// <summary>Each named output file from the run, written to the store as-is for download.</summary>
    RawBundle,

    /// <summary>HTML the job itself returned (an HTML stdout artifact or an emitted .html file),
    /// captured automatically — HTML is meant to be opened, not read as an escaped string.</summary>
    HtmlOutput,
}
