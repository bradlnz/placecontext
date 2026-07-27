namespace PlaceContext.Host.Components.ViewModels;

public sealed class ToolHistoryEntry
{
    public string ToolName { get; set; } = "";
    public bool Success { get; set; }
    public string Status { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
}
