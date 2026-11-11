using System.Text.Json;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Build;
using PlaceContext.Application.Features;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class BuildChatViewModel
{
    public bool AgentOpen { get; private set; }
    public bool AgentLoading { get; private set; }
    public string AgentTab { get; set; } = "Instructions";
    public string? AgentError { get; private set; }
    public string? AgentNotice { get; private set; }
    public DashboardSettings Dashboard { get; private set; } = new();
    public DashboardSettings AgentDraft { get; private set; } = new();
    public Guid AgentEditId { get; set; }
    public DashboardAgentProfile? EditedAgent => AgentDraft.Agents.FirstOrDefault(a => a.Id == AgentEditId);
    public IEnumerable<string> AgentModels => Connections.FirstOrDefault(c => c.Id == EditedAgent?.ConnectionId)?.Models ?? [];
    public IReadOnlyList<PlaceContext.Application.Mcp.McpConnectionInfo> AgentMcpConnections { get; private set; } = [];
    public string AgentMemory { get; set; } = "";
    public string AgentPrompt { get; private set; } = "";
    public string AgentTools { get; private set; } = "";
    public string NewActionTitle { get; set; } = "";
    public string NewActionPrompt { get; set; } = "";
    public IReadOnlyList<JobChainView> AgentWorkflows { get; private set; } = [];
    public static IEnumerable<LoopChainActionView> SubAgents(IEnumerable<JobChainStageView> stages)
    {
        foreach (var stage in stages)
        {
            if (stage.Action is LoopChainActionView { AgentProvider: "automatic" } action)
            {
                yield return action;
            }
            foreach (var nested in SubAgents(stage.ElseBranch ?? []))
            {
                yield return nested;
            }
        }
    }

    public async Task OpenAgentAsync()
    {
        if (AgentLoading)
        {
            return;
        }
        AgentOpen = true;
        CloseWorkflowPicker();
        OpenWorkspace();
        AgentLoading = true;
        AgentError = AgentNotice = null;
        AgentPrompt = AgentTools = AgentMemory = "";
        AgentDraft = new();
        AgentWorkflows = [];
        AgentMcpConnections = [];
        var projectId = Conversation.ProjectId;
        NotifyStateChanged();
        try
        {
            var settings = await store.GetSettingsAsync(projectId);
            var memory = await store.GetMemoryAsync(projectId);
            var inspection = await agent.InspectAsync(projectId, agentId: Conversation.AgentId);
            var mcpConnections = mcp is null ? [] : await mcp.ListConnectionsAsync(projectId);
            var workflows = await permissions.HasAsync(Permission.ChainsManage)
                ? await service.ListJobChainsAsync(projectId) : [];
            if (Conversation.ProjectId != projectId || !AgentOpen)
            {
                return;
            }
            Dashboard = settings;
            AgentDraft = JsonSerializer.Deserialize<DashboardSettings>(JsonSerializer.Serialize(settings))!;
            AgentEditId = Conversation.AgentId ?? Guid.Empty;
            if (EditedAgent is { Id: var id, ConnectionId: var connectionId } profile && id == Guid.Empty && connectionId == Guid.Empty)
            {
                profile.ConnectionId = Conversation.ConnectionId;
                profile.Model = Conversation.Model;
            }
            AgentMemory = memory;
            AgentPrompt = inspection.Prompt;
            AgentTools = inspection.Tools;
            AgentWorkflows = workflows;
            AgentMcpConnections = mcpConnections;
        }
        catch (Exception)
        {
            AgentError = "Could not load the agent configuration. Check project access and try again.";
        }
        finally
        {
            AgentLoading = false;
            NotifyStateChanged();
        }
    }

    public void ShowWorkspace() => AgentOpen = false;

    public void SelectAgent(string? value)
    {
        if (Locked || !Guid.TryParse(value, out var id) || !Dashboard.Agents.Any(a => a.Id == id))
        {
            return;
        }
        Conversation.AgentId = id;
        if (id == Guid.Empty && Dashboard.Agents.First(a => a.Id == id).ConnectionId == Guid.Empty)
        {
            SelectDefaultModel();
        }
        ApplyAgentModel();
        AgentOpen = false;
    }

    private void ApplyAgentModel()
    {
        var profile = Dashboard.Agents.FirstOrDefault(a => a.Id == (Conversation.AgentId ?? Guid.Empty));
        if (profile is { Id: var id, ConnectionId: var connectionId } && id == Guid.Empty && connectionId == Guid.Empty)
        {
            // Existing chats retain their model until the default agent is configured.
            return;
        }
        Conversation.ConnectionId = profile?.ConnectionId ?? Guid.Empty;
        Conversation.Model = profile?.Model ?? "";
    }

    public void AddAgent()
    {
        if (Locked || AgentLoading || AgentDraft.Agents.Count >= 8)
        {
            return;
        }
        var profile = new DashboardAgentProfile();
        profile.Name = Enumerable.Range(1, 9).Select(i => $"Agent {i}")
            .First(name => !AgentDraft.Agents.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        AgentDraft.Agents.Add(profile);
        AgentEditId = profile.Id;
    }

    public void SelectAgentConnection(string? value)
    {
        if (Locked || EditedAgent is not { } profile || !Guid.TryParse(value, out var id))
        {
            return;
        }
        profile.ConnectionId = id;
        profile.Model = AgentModels.FirstOrDefault() ?? "";
    }

    public void ToggleAgentMcp(Guid connectionId, bool assigned)
    {
        if (Locked || AgentLoading || EditedAgent is not { } profile)
        {
            return;
        }
        if (!assigned)
        {
            profile.McpConnectionIds.Remove(connectionId);
            return;
        }
        if (AgentMcpConnections.Any(c => c.Id == connectionId && c.Enabled)
            && !profile.McpConnectionIds.Contains(connectionId) && profile.McpConnectionIds.Count < 32)
        {
            profile.McpConnectionIds.Add(connectionId);
        }
    }

    public void AddDashboardAction()
    {
        var action = new DashboardAction(NewActionTitle.Trim(), NewActionPrompt.Trim());
        var proposed = new DashboardSettings { Instructions = AgentDraft.Instructions, Agents = AgentDraft.Agents, Actions = [.. AgentDraft.Actions, action] };
        try
        {
            proposed.Validate();
            AgentDraft = proposed;
            NewActionTitle = NewActionPrompt = "";
            AgentError = null;
        }
        catch (ArgumentException ex)
        {
            AgentError = ex.Message;
        }
    }

    public async Task SaveAgentAsync()
    {
        if (Locked || AgentLoading)
        {
            return;
        }
        AgentError = AgentNotice = null;
        AgentLoading = true;
        var projectId = Conversation.ProjectId;
        try
        {
            var saved = JsonSerializer.Deserialize<DashboardSettings>(JsonSerializer.Serialize(AgentDraft))!;
            await store.SaveSettingsAsync(projectId, saved);
            var inspection = await agent.InspectAsync(projectId, agentId: Conversation.AgentId);
            if (Conversation.ProjectId != projectId)
            {
                return;
            }
            Dashboard = saved;
            ApplyAgentModel();
            AgentPrompt = inspection.Prompt;
            AgentTools = inspection.Tools;
            AgentNotice = "Saved for your chats in this project. Agent settings apply on the next turn.";
        }
        catch (Exception ex)
        {
            AgentError = ex.Message;
        }
        finally
        {
            AgentLoading = false;
            NotifyStateChanged();
        }
    }

    public async Task SaveAgentMemoryAsync()
    {
        if (Locked || AgentLoading)
        {
            return;
        }
        AgentError = AgentNotice = null;
        AgentLoading = true;
        var projectId = Conversation.ProjectId;
        try
        {
            await store.SaveMemoryAsync(projectId, AgentMemory);
            var inspection = await agent.InspectAsync(projectId, agentId: Conversation.AgentId);
            if (Conversation.ProjectId != projectId)
            {
                return;
            }
            AgentPrompt = inspection.Prompt;
            AgentNotice = "Project memory saved.";
        }
        catch (Exception ex)
        {
            AgentError = ex.Message;
        }
        finally
        {
            AgentLoading = false;
            NotifyStateChanged();
        }
    }

    public async Task StartDashboardActionAsync(string prompt)
    {
        if (!CanStartChat || !string.IsNullOrWhiteSpace(Draft))
        {
            return;
        }
        AgentOpen = false;
        Draft = prompt;
        await SendAsync();
    }
}
