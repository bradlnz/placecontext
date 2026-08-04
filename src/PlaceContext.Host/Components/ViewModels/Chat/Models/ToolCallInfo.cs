namespace PlaceContext.Host.Components.ViewModels;

public sealed class ToolCallInfo
{
    private static int _nextId;
    public int Id { get; set; } = Interlocked.Increment(ref _nextId);
    public string ToolName { get; set; } = "";
    public string Args { get; set; } = "";
    public AgentToolCallStatus Status { get; set; }
    public string? Result { get; set; }
    public string ResultType { get; set; } = "text";
    public ChatResultKind ResultKind => ChatPresentationCatalog.ParseResultKind(ResultType);
    public int RetryCount { get; set; }
}
