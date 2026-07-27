namespace PlaceContext.Host.Components.ViewModels;

public sealed class AgentAction
{
    public string ToolName { get; set; } = "";
    public string Detail { get; set; } = "";
    public AgentToolCallStatus Status { get; set; }
}
