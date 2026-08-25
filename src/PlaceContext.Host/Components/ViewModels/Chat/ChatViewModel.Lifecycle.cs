using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Caching;
using PlaceContext.Infrastructure.Chat;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ChatViewModel
{
    // ── Lifecycle ────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        _ui.Set("Chat", "Agent inference");
        ProjectId = _ui.CurrentProjectId;
        ProjectName = _ui.CurrentProjectName ?? "";
        if (ProjectId.HasValue)
        {
            NewSession();
            NotifyStateChanged();
            // Fire-and-forget: populate sidebar data in the background so the UI renders immediately.
            _ = LoadAndRestoreSessionAsync();
        }
        else
        {
            WorkspaceLoaded = true;
        }
    }

    private async Task LoadAndRestoreSessionAsync()
    {
        try
        {
            await Task.WhenAll(
                LoadAgentConfigAsync(),
                LoadProjectChatStatusAsync(),
                LoadSessionsAsync(),
                LoadMcpConnectionsAsync(),
                LoadCommandsAsync(),
                LoadPanelArtifactsAsync()
            );
            await LoadTeamWorkspaceAsync();
            if (Sessions.Count > 0)
                await SelectSessionAsync(Sessions[0]);
        }
        catch { }
        WorkspaceLoaded = true;
        NotifyStateChanged();
    }

    public void OnProjectChanged()
    {
        if (ProjectId == _ui.CurrentProjectId)
            return;
        ProjectId = _ui.CurrentProjectId;
        ProjectName = _ui.CurrentProjectName ?? "";
        _ = OnProjectChangedAsync();
    }

    private async Task OnProjectChangedAsync()
    {
        if (!ProjectId.HasValue)
            return;
        WorkspaceLoaded = false;
        NotifyStateChanged();
        await LoadAgentConfigAsync();
        await LoadProjectChatStatusAsync();
        await LoadGraphAsync();
        ActiveActions.Clear();
        FetchedData.Clear();
        ToolHistory.Clear();
        await LoadSessionsAsync();
        await LoadMcpConnectionsAsync();
        await LoadCommandsAsync();
        await LoadPanelArtifactsAsync();
        await LoadTeamWorkspaceAsync();
        NewSession();
        WorkspaceLoaded = true;
        NotifyStateChanged();
    }

    private async Task LoadProjectChatStatusAsync()
    {
        if (ProjectId.HasValue)
            _chatStatus = await _projectChat.GetStatusAsync(ProjectId.Value);
    }

    private async Task LoadTeamWorkspaceAsync()
    {
        if (ProjectId is null)
            return;

        try
        {
            TeamAgents = await _svc.ListAgentDefinitionsAsync(ProjectId.Value);
            var jobs = await _svc.ListJobsAsync(ProjectId.Value);
            var projectJobIds = jobs.Select(job => job.Id).ToHashSet();
            TeamGoals = (await _svc.ListRecentRunReportsAsync(80))
                .Where(report => projectJobIds.Contains(report.JobId))
                .Where(report => !string.IsNullOrWhiteSpace(report.Run.Snapshot.Goal))
                .OrderByDescending(report => report.Run.StartedAt)
                .Take(8)
                .ToList();
        }
        catch
        {
            TeamAgents = Array.Empty<AgentDefinitionView>();
            TeamGoals = Array.Empty<RunReportView>();
        }
    }
}
