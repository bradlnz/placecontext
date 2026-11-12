namespace PlaceContext.Application.Build;

public sealed class ProjectAgentCatalog
{
    public int Version { get; set; }
    public List<DashboardAgentProfile> Agents { get; set; } = [];
    public List<AgentSkill> Skills { get; set; } = [];

    public void Validate()
    {
        if (Version < 0 || Agents is null || Agents.Count > 32 || Agents.Any(a => a is null || a.Id == Guid.Empty)
            || Agents.Select(a => a.Id).Distinct().Count() != Agents.Count
            || Agents.Select(a => a.Name?.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Agents.Count
            || Skills is null || Skills.Count > 32 || Skills.Any(s => s is null || s.Id == Guid.Empty
                || string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 60
                || string.IsNullOrWhiteSpace(s.Instructions) || s.Instructions.Length > 8000)
            || Skills.Select(s => s.Id).Distinct().Count() != Skills.Count
            || Skills.Select(s => s.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Skills.Count)
            throw new ArgumentException("Use up to 32 agents and skills with unique names. Skills need instructions up to 8,000 characters.");
        foreach (var agent in Agents)
        {
            agent.Validate();
            if (agent.SkillIds.Except(Skills.Select(s => s.Id)).Any())
                throw new ArgumentException("Assign only skills from this project.");
        }
    }
}

public sealed class AgentSkill
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Instructions { get; set; } = "";
}
