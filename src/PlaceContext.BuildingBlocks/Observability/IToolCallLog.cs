namespace PlaceContext.Application.Ports;

/// <summary>
/// In-memory ring buffer of recent MCP tool calls. A singleton so the MCP host (writer) and the
/// portal (reader) share one feed within the process.
/// </summary>
public interface IToolCallLog
{
    void Record(ToolCallEntry entry);
    IReadOnlyList<ToolCallEntry> Recent(int take = 100);
}
