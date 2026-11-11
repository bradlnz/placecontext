namespace PlaceContext.Application.Build;

public sealed class DashboardSettings
{
    public string Instructions { get; set; } = "";
    public List<DashboardAgentProfile> Agents { get; set; } = [new() { Id = Guid.Empty, Name = "Assistant" }];
    public List<DashboardAction> Actions { get; set; } =
    [
        new("Job statistics", "Show this project's job statistics using project_activity. Do not run or change anything."),
        new("See artifacts", "Show this project's recent artifacts and help me find the output I need. Do not run or change anything.")
    ];

    public void Validate()
    {
        if (Agents is null || Agents.Count is < 1 or > 8 || Agents.Any(a => a is null
                || string.IsNullOrWhiteSpace(a.Name) || a.Name.Length > 60
                || a.Model is null || a.Model.Length > 200 || a.Instructions is null || a.Instructions.Length > 8000
                || a.McpConnectionIds is null || a.McpConnectionIds.Count > 32 || a.McpConnectionIds.Contains(Guid.Empty)
                || a.McpConnectionIds.Distinct().Count() != a.McpConnectionIds.Count)
            || Agents.Select(a => a.Id).Distinct().Count() != Agents.Count
            || Agents.Select(a => a.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Agents.Count
            || !Agents.Any(a => a.Id == Guid.Empty))
        {
            throw new ArgumentException("Keep the default agent and use up to 8 agents with unique names up to 60 characters, models up to 200, instructions up to 8,000, and up to 32 distinct MCP connections each.");
        }
        if (Instructions is null || Instructions.Length > 8000 || Actions is null || Actions.Count > 8
            || Actions.Any(a => a is null || string.IsNullOrWhiteSpace(a.Title) || a.Title.Length > 60
                || string.IsNullOrWhiteSpace(a.Prompt) || a.Prompt.Length > 2000))
        {
            throw new ArgumentException("Use up to 8 tiles, with titles up to 60 characters, prompts up to 2,000, and instructions up to 8,000.");
        }
    }
}
