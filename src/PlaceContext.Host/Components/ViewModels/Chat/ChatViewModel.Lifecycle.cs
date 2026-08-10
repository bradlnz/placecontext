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
        NewSession();
        WorkspaceLoaded = true;
        NotifyStateChanged();
    }

    private async Task LoadProjectChatStatusAsync()
    {
        if (ProjectId.HasValue)
            _chatStatus = await _projectChat.GetStatusAsync(ProjectId.Value);
    }
}
