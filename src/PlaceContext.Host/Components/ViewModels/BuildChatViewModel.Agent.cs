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
    public IReadOnlyList<DashboardAgentProfile> ProjectAgents { get; private set; } = [];
    public IEnumerable<DashboardAgentProfile> AvailableAgents => Dashboard.Agents.Concat(ProjectAgents);
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

    public void CloseAgent() => AgentOpen = false;

    public void SelectAgent(string? value)
    {
        if (Locked || !Guid.TryParse(value, out var id) || !AvailableAgents.Any(a => a.Id == id))
        {
            return;
        }
        Conversation.AgentId = id;
        if ((id == Guid.Empty || ProjectAgents.Any(a => a.Id == id)) && AvailableAgents.First(a => a.Id == id).ConnectionId == Guid.Empty)
        {
            SelectDefaultModel();
        }
        ApplyAgentModel();
        AgentOpen = false;
    }

    private void ApplyAgentModel()
    {
        var profile = AvailableAgents.FirstOrDefault(a => a.Id == (Conversation.AgentId ?? Guid.Empty));
        if (profile is { ConnectionId: var connectionId } && connectionId == Guid.Empty
            && (profile.Id == Guid.Empty || ProjectAgents.Any(a => a.Id == profile.Id)))
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
        if (Locked || AgentLoading) return;
        var action = new DashboardAction(NewActionTitle.Trim(), NewActionPrompt.Trim());
        var proposed = JsonSerializer.Deserialize<DashboardSettings>(JsonSerializer.Serialize(AgentDraft))!;
        proposed.Actions.Add(action);
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

    public void EditDashboardAction(Guid id, string title, string prompt)
    {
        if (Locked || AgentLoading) return;
        var index = AgentDraft.Actions.FindIndex(a => a.Id == id);
        if (index >= 0) AgentDraft.Actions[index] = AgentDraft.Actions[index] with { Title = title, Prompt = prompt };
    }

    public void RemoveDashboardAction(DashboardAction action)
    {
        if (Locked || AgentLoading) return;
        AgentDraft.Actions.Remove(action);
        AgentDraft.TileOrder.Remove(action.Id.ToString());
        AgentDraft.TileSizes.Remove(action.Id.ToString());
    }

    public async Task MoveDashboardTileAsync(string key, int position)
    {
        if (Locked || AgentLoading || AgentOpen) return;
        var order = Dashboard.OrderedTiles().ToList();
        var index = order.IndexOf(key);
        if (index < 0 || position < 0 || position >= order.Count || index == position) return;
        order.RemoveAt(index);
        order.Insert(position, key);
        var saved = JsonSerializer.Deserialize<DashboardSettings>(JsonSerializer.Serialize(Dashboard))!;
        saved.TileOrder = order;
        await SaveDashboardAsync(saved);
    }

    [Microsoft.JSInterop.JSInvokable]
    public async Task ResizeDashboardTileAsync(string key, string size)
    {
        if (Locked || AgentLoading || AgentOpen || !Dashboard.TileKeys().Contains(key)
            || size is not ("square" or "wide" or "tall" or "large") || Dashboard.TileSize(key) == size) return;
        var saved = JsonSerializer.Deserialize<DashboardSettings>(JsonSerializer.Serialize(Dashboard))!;
        saved.TileSizes[key] = size;
        await SaveDashboardAsync(saved);
    }

    private async Task SaveDashboardAsync(DashboardSettings settings)
    {
        AgentLoading = true;
        Error = null;
        var projectId = Conversation.ProjectId;
        try
        {
            await store.SaveSettingsAsync(projectId, settings);
            if (Conversation.ProjectId == projectId) Dashboard = settings;
        }
        catch (Exception ex)
        {
            if (Conversation.ProjectId == projectId) Error = ex.Message;
        }
        finally
        {
            AgentLoading = false;
            NotifyStateChanged();
        }
    }

    public bool IsChartPinned(ChartPreview chart) => Dashboard.Charts.Any(c => c.Live == chart.Live && c.Sql == chart.Sql
        && ChartSpec.TryParse(c.ChartJson) is { } spec && spec.Title == chart.Spec.Title && spec.Type == chart.Spec.Type);

    public async Task PinDashboardChartAsync(ChartPreview chart)
    {
        if (Locked || AgentLoading || !chart.Live || IsChartPinned(chart)) return;
        var saved = JsonSerializer.Deserialize<DashboardSettings>(JsonSerializer.Serialize(Dashboard))!;
        saved.Charts.Add(new(chart.Spec.ToJson(), chart.Source) { Live = true, Sql = chart.Sql });
        await SaveDashboardAsync(saved);
        await RefreshDashboardChartsAsync();
    }

    public void RemoveDashboardChart(DashboardChart chart)
    {
        if (Locked || AgentLoading) return;
        AgentDraft.Charts.Remove(chart);
        AgentDraft.TileOrder.Remove(chart.Id.ToString());
        AgentDraft.TileSizes.Remove(chart.Id.ToString());
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
        OpenChat();
        Draft = prompt;
        await SendAsync();
    }
}
