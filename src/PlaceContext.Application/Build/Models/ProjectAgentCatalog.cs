namespace PlaceContext.Application.Build;

public sealed class ProjectAgentCatalog
{
    public int Version { get; set; }
    public List<DashboardAgentProfile> Agents { get; set; } = [];

    public void Validate()
    {
        if (Version < 0 || Agents is null || Agents.Count > 32 || Agents.Any(a => a is null || a.Id == Guid.Empty || a.Id == DashboardSettings.DefaultAgentId)
            || Agents.Select(a => a.Id).Distinct().Count() != Agents.Count
            || Agents.Select(a => a.Name?.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Agents.Count)
            throw new ArgumentException("Use up to 32 agents with unique names.");
        DashboardAgentProfile.ValidateHierarchy(Agents);
        foreach (var agent in Agents)
        {
            agent.Validate();
        }
    }
}
