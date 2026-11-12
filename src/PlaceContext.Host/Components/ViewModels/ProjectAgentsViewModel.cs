using System.Text.Json;
using PlaceContext.Application.Access;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Build;
using PlaceContext.Application.Mcp;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class ProjectAgentsViewModel(IProjectAgentStore store, IModelConnections models,
    IMcpClientService mcp, IPermissionService permissions, PortalUiState ui, IBuildChatStore? dashboardStore = null) : PageViewModel, IComponentViewModel
{
    public Guid ProjectId { get; private set; }
    public ProjectAgentCatalog Catalog { get; private set; } = new();
    public DashboardSettings? Dashboard { get; private set; }
    public IEnumerable<DashboardAgentProfile> Agents => (Dashboard?.Agents ?? []).Concat(Catalog.Agents);
    public bool EditingPrivateAgent { get; private set; }
    public bool CanEdit => EditingPrivateAgent || CanManage;
    public bool CanDelete => Existing && !(EditingPrivateAgent && Agent?.Id == Guid.Empty);
    public IReadOnlyList<ModelConnection> Connections { get; private set; } = [];
    public IReadOnlyList<McpConnectionInfo> Mcps { get; private set; } = [];
    public IEnumerable<string> Models => Connections.FirstOrDefault(c => c.Id == Agent?.ConnectionId)?.Models ?? [];
    public DashboardAgentProfile? Agent { get; private set; }
    public AgentSkill? Skill { get; private set; }
    public bool Loading { get; private set; }
    public bool Busy { get; private set; }
    public bool CanManage { get; private set; }
    public bool ShowingSkills { get; set; }
    public string AgentTab { get; set; } = "Overview";
    public bool ConfirmDelete { get; set; }
    public bool EditorOpen => Agent is not null || Skill is not null;
    public string? Error { get; private set; }
    public string? Notice { get; private set; }

    public async Task LoadAsync(Guid projectId)
    {
        if (Loading) return;
        Loading = true;
        Error = null;
        Close();
        ProjectId = projectId;
        Catalog = new();
        Dashboard = null;
        Connections = [];
        Mcps = [];
        ui.Set("Agents", "");
        try
        {
            Catalog = await store.GetAsync(projectId);
            Dashboard = dashboardStore is null ? null : await dashboardStore.GetSettingsAsync(projectId);
            CanManage = await permissions.HasAsync(Permission.ProjectsManage);
            Connections = (await models.ListAsync()).Where(c => c.Enabled).ToArray();
            Mcps = await mcp.ListConnectionsAsync(projectId);
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Loading = false; }
    }

    public void EditAgent(DashboardAgentProfile? agent = null)
    {
        if (Busy || Loading) return;
        Close();
        EditingPrivateAgent = agent is not null && Dashboard?.Agents.Any(a => a.Id == agent.Id) == true;
        Agent = agent is null ? new() { Name = "", WorkflowAccess = "read", DataAccess = "read" } : Copy(agent);
    }

    public void EditSkill(AgentSkill? skill = null)
    {
        if (Busy || Loading) return;
        Close();
        Skill = skill is null ? new() : Copy(skill);
    }

    public void Close()
    {
        if (Busy) return;
        Agent = null;
        Skill = null;
        ConfirmDelete = false;
        AgentTab = "Overview";
        EditingPrivateAgent = false;
        Error = null;
    }

    public void SelectConnection(string? value)
    {
        if (!CanEdit || Agent is null || !Guid.TryParse(value, out var id)) return;
        Agent.ConnectionId = id;
        Agent.Model = Models.FirstOrDefault() ?? "";
    }

    public void ToggleSkill(Guid id, bool selected)
    {
        if (!CanEdit || Busy || Agent is null) return;
        Agent.SkillIds.Remove(id);
        if (selected && Catalog.Skills.Any(s => s.Id == id)) Agent.SkillIds.Add(id);
    }

    public void ToggleMcp(Guid id, bool selected)
    {
        if (!CanEdit || Busy || Agent is null) return;
        Agent.McpConnectionIds.Remove(id);
        if (selected && Mcps.Any(m => m.Id == id && m.Enabled)) Agent.McpConnectionIds.Add(id);
    }

    public bool Existing => Agent is { } a ? Agents.Any(x => x.Id == a.Id)
        : Skill is { } s && Catalog.Skills.Any(x => x.Id == s.Id);

    public async Task SaveAsync(string instructions, bool delete = false)
    {
        if (!CanEdit || Busy || !EditorOpen || delete && (!ConfirmDelete || !CanDelete)) return;
        Busy = true;
        Error = Notice = null;
        try
        {
            if (EditingPrivateAgent && Agent is { } privateAgent && dashboardStore is not null)
            {
                var settings = await dashboardStore.GetSettingsAsync(ProjectId);
                var index = settings.Agents.FindIndex(a => a.Id == privateAgent.Id);
                if (index < 0) throw new InvalidOperationException("This private agent is unavailable. Refresh and try again.");
                if (delete) settings.Agents.RemoveAt(index);
                else
                {
                    var saved = Copy(privateAgent);
                    saved.Name = saved.Name.Trim();
                    saved.Instructions = instructions;
                    settings.Agents[index] = saved;
                }
                settings.Validate();
                await dashboardStore.SaveSettingsAsync(ProjectId, settings);
                Dashboard = settings;
                Agent = null;
                Notice = delete ? "Private agent removed." : "Private dashboard agent saved.";
                return;
            }
            var next = Copy(Catalog);
            if (Agent is { } agent)
            {
                next.Agents.RemoveAll(a => a.Id == agent.Id);
                if (!delete)
                {
                    var saved = Copy(agent);
                    saved.Name = saved.Name.Trim();
                    saved.Instructions = instructions;
                    next.Agents.Add(saved);
                }
            }
            if (Skill is { } skill)
            {
                next.Skills.RemoveAll(s => s.Id == skill.Id);
                if (delete)
                    foreach (var assignedAgent in next.Agents) assignedAgent.SkillIds.Remove(skill.Id);
                else
                    next.Skills.Add(new() { Id = skill.Id, Name = skill.Name.Trim(), Instructions = instructions });
            }
            next.Validate();
            await store.SaveAsync(ProjectId, next);
            Catalog = next;
            Agent = null;
            Skill = null;
            Notice = delete ? "Removed from this project." : "Saved to this project.";
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Busy = false; }
    }

    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
