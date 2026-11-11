namespace PlaceContext.Application.Build;

public sealed class DashboardAgentProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New agent";
    public Guid ConnectionId { get; set; }
    public string Model { get; set; } = "";
    public string Instructions { get; set; } = "";
    public List<Guid> McpConnectionIds { get; set; } = [];
}
