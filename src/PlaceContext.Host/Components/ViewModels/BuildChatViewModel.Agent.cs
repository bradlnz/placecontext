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
        OpenWorkspace();
        AgentLoading = true;
        AgentError = AgentNotice = null;
        AgentPrompt = AgentTools = AgentMemory = "";
        AgentDraft = new();
        AgentWorkflows = [];
        var projectId = Conversation.ProjectId;
        NotifyStateChanged();
        try
        {
            var settings = await store.GetSettingsAsync(projectId);
            var memory = await store.GetMemoryAsync(projectId);
            var inspection = await agent.InspectAsync(projectId);
            var workflows = await permissions.HasAsync(Permission.ChainsManage)
                ? await service.ListJobChainsAsync(projectId) : [];
            if (Conversation.ProjectId != projectId || !AgentOpen)
            {
                return;
            }
            Dashboard = settings;
            AgentDraft = JsonSerializer.Deserialize<DashboardSettings>(JsonSerializer.Serialize(settings))!;
            AgentMemory = memory;
            AgentPrompt = inspection.Prompt;
            AgentTools = inspection.Tools;
            AgentWorkflows = workflows;
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

    public void AddDashboardAction()
    {
        var action = new DashboardAction(NewActionTitle.Trim(), NewActionPrompt.Trim());
        var proposed = new DashboardSettings { Instructions = AgentDraft.Instructions, Actions = [.. AgentDraft.Actions, action] };
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
            var inspection = await agent.InspectAsync(projectId);
            if (Conversation.ProjectId != projectId)
            {
                return;
            }
            Dashboard = saved;
            AgentPrompt = inspection.Prompt;
            AgentNotice = "Saved for your chats in this project. Instructions apply on the next turn.";
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
            var inspection = await agent.InspectAsync(projectId);
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
