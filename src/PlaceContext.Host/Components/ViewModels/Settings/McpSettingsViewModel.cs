using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Domain.Mcp;
using PlaceContext.Host;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class McpSettingsViewModel(
    IPlaceContextService service,
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
    public string Name { get; set; } = "";
    public string Transport { get; set; } = McpTransport.Http;
    public string Endpoint { get; set; } = "";
    public string Command { get; set; } = "";
    public string Args { get; set; } = "";
    public string AuthType { get; set; } = McpAuthType.None;
    public string AuthToken { get; set; } = "";
    public string AuthHeader { get; set; } = "";
    public string OAuthScopes { get; set; } = "";
    public bool IsStdio => Transport == McpTransport.Stdio;
    public bool HasTokenAuth => AuthType is McpAuthType.Bearer or McpAuthType.ApiKey;
    public bool IsHeaderAuth => AuthType == McpAuthType.Header;
    public bool IsOAuth => AuthType == McpAuthType.OAuth;

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
        Name = Endpoint = Command = Args = AuthToken = AuthHeader = OAuthScopes = "";
        Transport = McpTransport.Http;
        AuthType = McpAuthType.None;
        ShowAdd = true;
        NotifyStateChanged();
    }

    public void CancelAdd()
    {
        ShowAdd = false;
        NotifyStateChanged();
    }

    public async Task AddAsync()
    {
        if (ProjectId is null)
            return;
        try
        {
            await service.CreateMcpConnectionAsync(
                new(
                    ProjectId.Value,
                    Name,
                    Transport,
                    IsStdio ? null : Endpoint,
                    IsStdio ? Command : null,
                    IsStdio ? Args : null,
                    AuthType,
                    AuthToken,
                    AuthHeader,
                    null,
                    OAuthScopes
                )
            );
            ShowAdd = false;
            Message = "MCP server added.";
            await LoadConnectionsAsync();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
            NotifyStateChanged();
        }
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
