namespace PlaceContext.Host.Components.ViewModels;

public sealed class AgentMessage
{
    private static int _nextId;
    public AgentMessage(string role, string content)
    {
        Id = Interlocked.Increment(ref _nextId);
        Role = role;
        Content = content;
    }
    public int Id { get; }
    public string Role { get; }
    public string Content { get; }
    public string? Thinking { get; set; }
    public List<ToolCallInfo> ToolCalls { get; } = new();
    public string? AttachmentName { get; set; }
    public string? AttachmentKey { get; set; }
    public string? AttachmentContentType { get; set; }
    public long AttachmentSizeBytes { get; set; }
}
