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
    public string AgentTab { get; set; } = "Agent";
    public string? AgentError { get; private set; }
    public string? AgentNotice { get; private set; }
    public DashboardSettings Dashboard { get; private set; } = new();
    public IReadOnlyList<DashboardAgentProfile> ProjectAgents { get; private set; } = [];
    public IEnumerable<DashboardAgentProfile> DashboardAgents => AvailableAgents;
    public DashboardLayout Tiles => Dashboard.ForAgent(Conversation.AgentId);
    public IEnumerable<string> DashboardTileKeys => Tiles.OrderedTiles().Where(key => key is not ("chat" or "chart"));
    public DashboardLayout DraftTiles => AgentDraft.ForAgent(Conversation.AgentId);
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
    public string? EditingTileKey { get; private set; }
    public string TileTitleDraft { get; set; } = "";
    public string TileDescriptionDraft { get; set; } = "";
    public string TilePromptDraft { get; set; } = "";
    public string? TileEditError { get; private set; }

    public void EditDashboardTile(string key)
    {
        if (Locked || AgentLoading || AgentOpen || Tiles.TileAction(key) is not { } action) return;
        EditingTileKey = key;
        TileTitleDraft = action.Title;
        TileDescriptionDraft = action.Description ?? "";
        TilePromptDraft = action.Prompt;
        TileEditError = null;
    }

    public void CloseTileEditor()
    {
        if (AgentLoading) return;
        EditingTileKey = null;
        TileEditError = null;
    }

    public async Task SaveDashboardTileAsync()
    {
        if (Locked || AgentLoading || EditingTileKey is not { } key || Tiles.TileAction(key) is not { } action) return;
        var saved = JsonSerializer.Deserialize<DashboardSettings>(JsonSerializer.Serialize(Dashboard))!;
        var tiles = saved.ForAgent(Conversation.AgentId);
        var edited = action with { Title = TileTitleDraft.Trim(), Description = string.IsNullOrWhiteSpace(TileDescriptionDraft) ? null : TileDescriptionDraft.Trim(), Prompt = TilePromptDraft.Trim() };
        if (tiles.BuiltInTiles.ContainsKey(key)) tiles.BuiltInTiles[key] = edited;
        else tiles.Actions[tiles.Actions.FindIndex(a => a.Id == action.Id)] = edited;
        try { saved.Validate(); }
        catch (ArgumentException ex) { TileEditError = ex.Message; return; }
        await SaveDashboardAsync(saved);
        if (ReferenceEquals(Dashboard, saved)) CloseTileEditor();
        else TileEditError = Error ?? "The tile could not be saved. Please try again.";
    }
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
            AgentEditId = Conversation.AgentId ?? DashboardSettings.DefaultAgentId;
            if (EditedAgent is { Id: var id, ConnectionId: var connectionId } profile && id == DashboardSettings.DefaultAgentId && connectionId == Guid.Empty)
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
        if (Locked || AgentLoading || ChartQuerySaving || !Guid.TryParse(value, out var id) || !DashboardAgents.Any(a => a.Id == id))
        {
            return;
        }
        if (Conversation.AgentId == id) return;
        CloseTileEditor();
        Conversation.AgentId = id;
        ResetDashboardCharts();
        _ = RefreshDashboardChartsAsync();
        if (id == DashboardSettings.DefaultAgentId && DashboardAgents.First(a => a.Id == id).ConnectionId == Guid.Empty)
        {
            SelectDefaultModel();
        }
        ApplyAgentModel();
        AgentOpen = false;
        if (WorkflowPickerOpen)
        {
            _ = OpenWorkflowPickerAsync();
        }
    }

    private void ApplyAgentModel()
    {
        var profile = AvailableAgents.FirstOrDefault(a => a.Id == (Conversation.AgentId ?? DashboardSettings.DefaultAgentId));
        if (profile is { ConnectionId: var connectionId } && connectionId == Guid.Empty
            && (profile.Id == DashboardSettings.DefaultAgentId || ProjectAgents.Any(a => a.Id == profile.Id)))
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
        proposed.ForAgent(Conversation.AgentId).Actions.Add(action);
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
        var index = DraftTiles.Actions.FindIndex(a => a.Id == id);
        if (index >= 0) DraftTiles.Actions[index] = DraftTiles.Actions[index] with { Title = title, Prompt = prompt };
    }

    public void RemoveDashboardAction(DashboardAction action)
    {
        if (Locked || AgentLoading) return;
        DraftTiles.Actions.Remove(action);
        DraftTiles.HiddenTiles.Remove(action.Id.ToString());
        DraftTiles.TileOrder.Remove(action.Id.ToString());
        DraftTiles.TileRowStarts.Remove(action.Id.ToString());
        DraftTiles.TileSizes.Remove(action.Id.ToString());
        DraftTiles.TilePositions.Remove(action.Id.ToString());
    }

    public async Task RemoveDashboardTileAsync(string key)
    {
        if (Locked || AgentLoading || AgentOpen || !Tiles.OrderedTiles().Contains(key)) return;
        var saved = JsonSerializer.Deserialize<DashboardSettings>(JsonSerializer.Serialize(Dashboard))!;
        saved.ForAgent(Conversation.AgentId).HiddenTiles.Add(key);
        await SaveDashboardAsync(saved);
    }

    public void RestoreDashboardTile(string key)
    {
        if (Locked || AgentLoading) return;
        DraftTiles.HiddenTiles.Remove(key);
    }

    [Microsoft.JSInterop.JSInvokable]
    public async Task<bool> MoveDashboardTileAsync(string key, int position, bool newRow = false, int? column = null, int? row = null, Dictionary<string, DashboardTilePosition>? positions = null)
    {
        if (Locked || AgentLoading || AgentOpen || (column is null) != (row is null)
            || column is < 1 or > 32 || row is < 1 or > 100) return false;
        // Browser positions and indices cover only the tiles rendered on the dashboard.
        var order = (positions is null ? Tiles.OrderedTiles() : DashboardTileKeys).ToList();
        if (positions is not null && (positions.Count != order.Count || positions.Keys.Except(order).Any())) return false;
        var index = order.IndexOf(key);
        if (index < 0 || position < 0 || position >= order.Count || index == position && column is null && !newRow && !Tiles.TileRowStarts.Contains(key)) return false;
        order.RemoveAt(index);
        order.Insert(position, key);
        var saved = JsonSerializer.Deserialize<DashboardSettings>(JsonSerializer.Serialize(Dashboard))!;
        saved.ForAgent(Conversation.AgentId).TileOrder = order;
        if (positions is not null) foreach (var (tile, cell) in positions) saved.ForAgent(Conversation.AgentId).TilePositions[tile] = cell;
        if (column is { } x && row is { } y) saved.ForAgent(Conversation.AgentId).TilePositions[key] = new(x, y);
        else saved.ForAgent(Conversation.AgentId).TilePositions.Remove(key);
        saved.ForAgent(Conversation.AgentId).TileRowStarts.Remove(key);
        if (newRow) saved.ForAgent(Conversation.AgentId).TileRowStarts.Add(key);
        await SaveDashboardAsync(saved);
        return ReferenceEquals(Dashboard, saved);
    }

    [Microsoft.JSInterop.JSInvokable]
    public async Task<bool> ResizeDashboardTileAsync(string key, string size)
    {
        if (Locked || AgentLoading || AgentOpen || !Tiles.TileKeys().Contains(key)
            || size is not ("square" or "wide" or "tall" or "large") || Tiles.TileSize(key) == size) return false;
        var saved = JsonSerializer.Deserialize<DashboardSettings>(JsonSerializer.Serialize(Dashboard))!;
        saved.ForAgent(Conversation.AgentId).TileSizes[key] = size;
        await SaveDashboardAsync(saved);
        return ReferenceEquals(Dashboard, saved);
    }

    private async Task SaveDashboardAsync(DashboardSettings settings)
    {
        AgentLoading = true;
        Error = null;
        var projectId = Conversation.ProjectId;
        foreach (var id in settings.AgentLayouts.Keys.Except(AvailableAgents.Select(a => a.Id)).ToArray()) settings.AgentLayouts.Remove(id);
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

    public bool IsChartPinned(ChartPreview chart) => Tiles.Charts.Any(c => c.Live == chart.Live && c.Sql == chart.Sql
        && ChartSpec.TryParse(c.ChartJson) is { } spec && spec.Title == chart.Spec.Title && spec.Type == chart.Spec.Type);

    public async Task PinDashboardChartAsync(ChartPreview chart)
    {
        if (Locked || AgentLoading || !chart.Live || IsChartPinned(chart)) return;
        var saved = JsonSerializer.Deserialize<DashboardSettings>(JsonSerializer.Serialize(Dashboard))!;
        saved.ForAgent(Conversation.AgentId).Charts.Add(new(chart.Spec.ToJson(), chart.Source) { Live = true, Sql = chart.Sql });
        await SaveDashboardAsync(saved);
        await RefreshDashboardChartsAsync();
    }

    public void RemoveDashboardChart(DashboardChart chart)
    {
        if (Locked || AgentLoading) return;
        DraftTiles.Charts.Remove(chart);
        DraftTiles.HiddenTiles.Remove(chart.Id.ToString());
        DraftTiles.TileOrder.Remove(chart.Id.ToString());
        DraftTiles.TileRowStarts.Remove(chart.Id.ToString());
        DraftTiles.TileSizes.Remove(chart.Id.ToString());
        DraftTiles.TilePositions.Remove(chart.Id.ToString());
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
