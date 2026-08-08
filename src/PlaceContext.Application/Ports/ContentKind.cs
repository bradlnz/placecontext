namespace PlaceContext.Application.Ports;

/// <summary>
/// Stable kind strings for universal RAG content. Every kind is embeddable and its source text
/// is encrypted at rest; vectors live in pgvector for cosine search.
/// </summary>
public static class ContentKind
{
    public const string RunOutput = "run_output";
    public const string ProjectData = "project_data";
    public const string Decision = "decision";
    public const string Activity = "activity";
    public const string Requirements = "requirements";
    public const string Event = "event";
    public const string Chart = "chart";
    public const string Document = "document";
    public const string GraphNode = "graph_node";
}
