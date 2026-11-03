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
    public Guid ConnectionId
    {
        get; set;
    }
    public string Model { get; set; } = "";
    public JsonArray Messages { get; set; } = [];
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
