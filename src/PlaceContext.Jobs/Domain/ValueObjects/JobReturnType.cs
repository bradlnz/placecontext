namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// The declared type of a job's primary return (the data its containers print to stdout /
/// <c>result.json</c>). Every job has one, and it determines the artifact generated for every run —
/// artifact generation is mandatory, not opt-in. Generic — no domain knowledge.
/// </summary>
public enum JobReturnType
{
    /// <summary>The return is JSON data — stored verbatim as <c>result.json</c>.</summary>
    Json,

    /// <summary>The return is tabular data — rendered as a self-contained HTML report.</summary>
    Table,

    /// <summary>The return is a numeric series — rendered as an inline-SVG chart page.</summary>
    Chart,

    /// <summary>The return is an HTML document — stored openable as-is.</summary>
    Html,

    /// <summary>The return is row data — flattened to a CSV export.</summary>
    Csv,

    /// <summary>The return is plain text — stored verbatim as <c>result.txt</c>.</summary>
    Text,

    /// <summary>The return is a PDF document emitted to <c>/out</c> — stored openable as-is.</summary>
    Pdf,

    /// <summary>The return is an image emitted to <c>/out</c> — stored with its image content type.</summary>
    Image,

    /// <summary>The return is a video emitted to <c>/out</c> — stored with its video content type.</summary>
    Video,
}
