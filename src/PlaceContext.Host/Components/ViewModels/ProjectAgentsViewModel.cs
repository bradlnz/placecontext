using System.Text.Json;
using Microsoft.AspNetCore.Components;
using PlaceContext.Application;
using PlaceContext.Application.Access;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Build;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Mcp;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class ProjectAgentsViewModel(IProjectAgentStore store, IModelConnections models,
    IMcpClientService mcp, IPermissionService permissions, PortalUiState ui, IBuildChatStore? dashboardStore = null,
    PlaceContextService? service = null, NavigationManager? navigation = null) : PageViewModel, IComponentViewModel
{
    public Guid ProjectId { get; private set; }
    public ProjectAgentCatalog Catalog { get; private set; } = new();
    public DashboardSettings? Dashboard { get; private set; }
    public IEnumerable<DashboardAgentProfile> Agents => (Dashboard?.Agents ?? []).Concat(Catalog.Agents);
    public IEnumerable<DashboardAgentProfile> ParentAgents => (EditingPrivateAgent ? Dashboard?.Agents ?? [] : Catalog.Agents)
        .Where(a => a.Id != Agent?.Id && a.Id != DashboardSettings.DefaultAgentId);
    public bool EditingPrivateAgent { get; private set; }
    public bool CanEdit => EditingPrivateAgent || CanManage;
    public IReadOnlyList<ModelConnection> Connections { get; private set; } = [];
    public IReadOnlyList<McpConnectionInfo> Mcps { get; private set; } = [];
    public IReadOnlyList<JobChainView> Workflows { get; private set; } = [];
    public bool CanViewWorkflows { get; private set; }
    public IEnumerable<string> Models => Connections.FirstOrDefault(c => c.Id == Agent?.ConnectionId)?.Models ?? [];
    public DashboardAgentProfile? Agent { get; private set; }
    public bool Loading { get; private set; }
    public bool Busy { get; private set; }
    public bool CanManage { get; private set; }
    public string AgentTab { get; set; } = "Overview";
    public Guid? ConfirmDeleteId { get; set; }
    public bool EditorOpen => Agent is not null;
    public string? Error { get; private set; }
    public string? Notice { get; private set; }
    private Task _pageLoad = Task.CompletedTask;
    private int _pageRequest;

    public async Task LoadPageAsync(Guid projectId, string? section)
    {
        var request = ++_pageRequest;
        await _pageLoad;
        if (request != _pageRequest) return;
        await (_pageLoad = LoadAsync(projectId));
        if (request != _pageRequest) return;
        if (Error is not null || section is null) return;
        if (section == "new")
        {
            if (CanManage) EditAgent();
            else Error = "Project management access is required to create agents.";
        }
        else if (Guid.TryParse(section, out var id) && Agents.FirstOrDefault(a => a.Id == (id == Guid.Empty ? DashboardSettings.DefaultAgentId : id)) is { } agent)
            EditAgent(agent);
        else Error = "Agent not found in this project.";
    }

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
        Workflows = [];
        CanViewWorkflows = false;
        ui.Set("Agents", "");
        try
        {
            Catalog = await store.GetAsync(projectId);
            Dashboard = dashboardStore is null ? null : await dashboardStore.GetSettingsAsync(projectId);
            CanManage = await permissions.HasAsync(Permission.ProjectsManage);
            Connections = (await models.ListAsync()).Where(c => c.Enabled).ToArray();
            Mcps = await mcp.ListConnectionsAsync(projectId);
            CanViewWorkflows = await permissions.HasAsync(Permission.ChainsManage);
            if (CanViewWorkflows && service is not null) Workflows = await service.ListJobChainsAsync(projectId);
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
        Agent.TableAccess ??= Agent.EffectiveTableAccess;
        Agent.SourceAccess ??= Agent.EffectiveSourceAccess;
        Agent.ArtifactAccess ??= Agent.EffectiveArtifactAccess;
    }

    public void Close()
    {
        if (Busy) return;
        Agent = null;
        ConfirmDeleteId = null;
        AgentTab = "Overview";
        EditingPrivateAgent = false;
        Error = null;
    }

    public void OpenAgent(Guid id) => navigation?.NavigateTo($"/project/{ProjectId}/agents/{id}");

    public void CreateAgent() => navigation?.NavigateTo($"/project/{ProjectId}/agents/new");

    public void CloseEditor(string? section)
    {
        Close();
        if (section is not null) navigation?.NavigateTo($"/project/{ProjectId}/agents");
    }

    public async Task DeleteAgentAsync(Guid id, string? section)
    {
        await DeleteAsync(id);
        if (section == id.ToString()) navigation?.NavigateTo($"/project/{ProjectId}/agents");
    }

    public async Task SaveAgentAsync(string instructions, string? section)
    {
        await SaveAsync(instructions);
        if (Error is null && !EditorOpen && section is not null) navigation?.NavigateTo($"/project/{ProjectId}/agents");
    }

    public void SelectConnection(string? value)
    {
        if (!CanEdit || Agent is null || !Guid.TryParse(value, out var id)) return;
        Agent.ConnectionId = id;
        Agent.Model = Models.FirstOrDefault() ?? "";
    }

    public void ToggleMcp(Guid id, bool selected)
    {
        if (!CanEdit || Busy || Agent is null) return;
        Agent.McpConnectionIds.Remove(id);
        if (selected && Mcps.Any(m => m.Id == id && m.Enabled)) Agent.McpConnectionIds.Add(id);
    }

    public void ToggleSourceCapability(string capability, bool selected)
    {
        if (!CanEdit || Busy || Agent is null || capability is not ("metadata" or "search" or "connect" or "pipeline")) return;
        Agent.SourceCapabilities ??= Agent.EffectiveSourceCapabilities.ToList();
        Agent.SourceCapabilities.Remove(capability);
        if (selected) Agent.SourceCapabilities.Add(capability);
    }

    public void ToggleWorkflow(Guid id, bool selected)
    {
        if (!CanEdit || !CanViewWorkflows || Busy || Agent is null) return;
        Agent.WorkflowIds.Remove(id);
        if (selected && Workflows.Any(w => w.Id == id)) Agent.WorkflowIds.Add(id);
    }

    public bool Existing => Agent is { } a && Agents.Any(x => x.Id == a.Id);

    public bool CanEditAgent(DashboardAgentProfile agent) =>
        CanManage || Dashboard?.Agents.Any(a => a.Id == agent.Id) == true;

    public bool CanDeleteAgent(DashboardAgentProfile agent) =>
        agent.Id != DashboardSettings.DefaultAgentId && CanEditAgent(agent);

    public async Task SaveAsync(string instructions)
    {
        if (!CanEdit || Busy || !EditorOpen) return;
        Busy = true;
        Error = Notice = null;
        try
        {
            if (EditingPrivateAgent && Agent is { } privateAgent && dashboardStore is not null)
            {
                var settings = await dashboardStore.GetSettingsAsync(ProjectId);
                var index = settings.Agents.FindIndex(a => a.Id == privateAgent.Id);
                if (index < 0) throw new InvalidOperationException("This private agent is unavailable. Refresh and try again.");
                var saved = Copy(privateAgent);
                saved.Name = saved.Name.Trim();
                saved.Instructions = instructions;
                settings.Agents[index] = saved;
                settings.Validate();
                await dashboardStore.SaveSettingsAsync(ProjectId, settings);
                Dashboard = settings;
                Agent = null;
                Notice = "Private dashboard agent saved.";
                return;
            }
            if (Agent is not { } agent) return;
            var next = Copy(Catalog);
            next.Agents.RemoveAll(a => a.Id == agent.Id);
            var shared = Copy(agent);
            shared.Name = shared.Name.Trim();
            shared.Instructions = instructions;
            next.Agents.Add(shared);
            next.Validate();
            await store.SaveAsync(ProjectId, next);
            Catalog = next;
            Agent = null;
            Notice = "Saved to this project.";
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Busy = false; }
    }

    public async Task DeleteAsync(Guid id)
    {
        if (Busy || Loading) return;
        var agent = Agents.FirstOrDefault(a => a.Id == id);
        if (agent is null || !CanDeleteAgent(agent)) return;
        Busy = true;
        Error = Notice = null;
        try
        {
            string notice;
            if (Dashboard?.Agents.Any(a => a.Id == id) == true)
            {
                if (dashboardStore is null) throw new InvalidOperationException("This private agent is unavailable. Refresh and try again.");
                var settings = await dashboardStore.GetSettingsAsync(ProjectId);
                var index = settings.Agents.FindIndex(a => a.Id == id);
                if (index < 0) throw new InvalidOperationException("This private agent is unavailable. Refresh and try again.");
                settings.Agents.RemoveAt(index);
                settings.AgentLayouts.Remove(id);
                settings.Validate();
                await dashboardStore.SaveSettingsAsync(ProjectId, settings);
                Dashboard = settings;
                notice = "Private dashboard agent removed.";
            }
            else
            {
                var next = Copy(Catalog);
                next.Agents.RemoveAll(a => a.Id == id);
                next.Validate();
                await store.SaveAsync(ProjectId, next);
                Catalog = next;
                notice = "Removed from this project.";
            }
            ConfirmDeleteId = null;
            if (Agent?.Id == id)
            {
                Agent = null;
                AgentTab = "Overview";
                EditingPrivateAgent = false;
            }
            Notice = notice;
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Busy = false; }
    }

    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
