using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Domain.Mcp;
using PlaceContext.Host;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class McpSettingsViewModel(
    PlaceContextService service,
    PortalUiState ui,
    IJSRuntime js
) : PageViewModel, IDisposable
{
    public const string OAuthStartRoute = "/mcp-oauth/start?connectionId=";
    public IReadOnlyList<ProjectSummaryView> Projects { get; private set; } =
        Array.Empty<ProjectSummaryView>();
    public IReadOnlyList<McpConnectionView> Connections { get; private set; } =
        Array.Empty<McpConnectionView>();
    public Guid? ProjectId { get; private set; }
    public bool Loading { get; private set; } = true;
    public bool ShowAdd { get; private set; }
    public string? Message { get; private set; }
    public bool IsOAuthConnected(McpConnectionView connection) =>
        connection.LastStatus?.StartsWith("oauth:connected", StringComparison.Ordinal) == true;

    public bool IsExpired(McpConnectionView connection) =>
        Presentation.IsExpired(connection.OAuthTokenExpiresAt);

    public async Task LoadAsync()
    {
        ui.Set("Settings", "MCP servers");
        Projects = await service.GetProjectsAsync();
        ProjectId = Projects.FirstOrDefault()?.Id;
        await LoadConnectionsAsync();
    }

    public async Task ProjectChangedAsync(Guid? projectId)
    {
        ProjectId = projectId;
        ShowAdd = false;
        await LoadConnectionsAsync();
    }

    public Task ProjectChangedAsync(ChangeEventArgs args) =>
        ProjectChangedAsync(Guid.TryParse(args.Value?.ToString(), out var id) ? id : null);

    public void ShowAddMcp()
    {
        ShowAdd = true;
        NotifyStateChanged();
    }

    public void CancelAdd()
    {
        ShowAdd = false;
        NotifyStateChanged();
    }

    public async Task AddedAsync(McpConnectionView connection)
    {
        ShowAdd = false;
        Message = $"{connection.Name} added.";
        await LoadConnectionsAsync();
    }

    public async Task TestAsync(Guid id)
    {
        try
        {
            var result = await service.TestMcpConnectionAsync(id);
            Message = $"{result.Name}: {result.LastStatus ?? "unknown"}";
            await LoadConnectionsAsync();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
            NotifyStateChanged();
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        try
        {
            await service.DeleteMcpConnectionAsync(id);
            Message = "MCP server deleted.";
            await LoadConnectionsAsync();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
            NotifyStateChanged();
        }
    }

    public Task StartOAuthFlowAsync(Guid id) =>
        js.InvokeVoidAsync("open", $"{OAuthStartRoute}{id}", "_blank").AsTask();

    public async Task AfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;
        await js.InvokeVoidAsync(
            "eval",
            "if (!window.__settingsMcpOAuthListener) { window.__settingsMcpOAuthListener = function(e) { if (e.data && e.data.startsWith && e.data.startsWith('mcp-oauth-')) DotNet.invokeMethodAsync('PlaceContext.Host', 'OnSettingsMcpOAuthCallback', e.data); }; window.addEventListener('message', window.__settingsMcpOAuthListener); }"
        );
    }

    [JSInvokable]
    public async Task OAuthCallbackAsync(string message) => await LoadConnectionsAsync();

    public void Dispose() { }

    private async Task LoadConnectionsAsync()
    {
        Loading = true;
        NotifyStateChanged();
        try
        {
            Connections = ProjectId is { } id
                ? await service.ListMcpConnectionsAsync(id)
                : Array.Empty<McpConnectionView>();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            Loading = false;
            NotifyStateChanged();
        }
    }
}
