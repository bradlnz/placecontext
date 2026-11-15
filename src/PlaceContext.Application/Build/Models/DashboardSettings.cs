namespace PlaceContext.Application.Build;

public sealed class DashboardSettings : DashboardLayout, System.Text.Json.Serialization.IJsonOnDeserialized
{
    public static readonly Guid DefaultAgentId = new("c471b57a-35ae-4c91-a9d5-45783182b874");

    public string Instructions { get; set; } = "";
    public List<DashboardAgentProfile> Agents { get; set; } = [new() { Id = DefaultAgentId, Name = "Master agent" }];
    public Dictionary<Guid, DashboardLayout> AgentLayouts { get; set; } = [];

    public DashboardLayout ForAgent(Guid? agentId)
    {
        if (agentId is null || agentId == Guid.Empty || agentId == DefaultAgentId) return this;
        if (!AgentLayouts.TryGetValue(agentId.Value, out var layout))
            AgentLayouts[agentId.Value] = layout = new();
        return layout;
    }

    void System.Text.Json.Serialization.IJsonOnDeserialized.OnDeserialized()
    {
        // Preserve settings saved before the default agent had a nonempty identity.
        foreach (var agent in Agents ?? [])
        {
            if (agent is { Id: var id } && id == Guid.Empty) agent.Id = DefaultAgentId;
            if (agent?.Id == DefaultAgentId && agent.Name is "Assistant" or "Dashboard agent") agent.Name = "Master agent";
        }
    }

    public void Validate()
    {
        foreach (var agent in Agents ?? []) agent?.Validate();
        if (Agents is null || Agents.Count is < 1 or > 8 || Agents.Any(a => a is null || a.Id == Guid.Empty
                || string.IsNullOrWhiteSpace(a.Name) || a.Name.Length > 60
                || a.Model is null || a.Model.Length > 200 || a.Instructions is null || a.Instructions.Length > 8000
                || a.McpConnectionIds is null || a.McpConnectionIds.Count > 32 || a.McpConnectionIds.Contains(Guid.Empty)
                || a.McpConnectionIds.Distinct().Count() != a.McpConnectionIds.Count)
            || Agents.Select(a => a.Id).Distinct().Count() != Agents.Count
            || Agents.Select(a => a.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Agents.Count
            || !Agents.Any(a => a.Id == DefaultAgentId))
        {
            throw new ArgumentException("Keep the default agent and use up to 8 agents with unique names up to 60 characters, models up to 200, instructions up to 8,000, and up to 32 distinct MCP connections each.");
        }
        DashboardAgentProfile.ValidateHierarchy(Agents);
        if (Instructions is null || Instructions.Length > 8000)
            throw new ArgumentException("Use instructions up to 8,000 characters.");
        ValidateLayout();
        if (AgentLayouts is null || AgentLayouts.Count > 40 || AgentLayouts.Any(p => p.Key == Guid.Empty || p.Key == DefaultAgentId || p.Value is null))
            throw new ArgumentException("Use up to 40 agent dashboard layouts with valid agent IDs.");
        foreach (var layout in AgentLayouts.Values) layout.ValidateLayout();
    }
}
