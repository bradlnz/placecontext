using System.Text.Json.Nodes;

namespace PlaceContext.Application.Build;

public sealed class BuildConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId
    {
        get; set;
    }
    public string Title { get; set; } = "New chat";
    public Guid? ParentConversationId { get; set; }
    public int? ParentMessageCount { get; set; }
    public Guid? AgentId { get; set => field = value == Guid.Empty ? DashboardSettings.DefaultAgentId : value; }
    public Guid ConnectionId
    {
        get; set;
    }
    public string Model { get; set; } = "";
    public JsonArray Messages { get; set; } = [];
    public string? SystemPrompt { get; set; }
    public string? ToolSchema { get; set; }
    public BuildRunStatus Status
    {
        get; set;
    }
    public string? Error
    {
        get; set;
    }
    public bool StopRequested
    {
        get; set;
    }
}
