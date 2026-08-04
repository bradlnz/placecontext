using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Mcp;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Caching;
using PlaceContext.Infrastructure.Chat;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ChatViewModel
{
    // ── MCP connections ──────────────────────────────────────────────────────

    public async Task LoadMcpConnectionsAsync()
    {
        if (!ProjectId.HasValue)
            return;
        try
        {
            McpConnections = await _svc.ListMcpConnectionsAsync(ProjectId.Value);
        }
        catch
        {
            McpConnections = Array.Empty<McpConnectionView>();
        }
        NotifyStateChanged();
    }

    public void ShowAddMcpForm()
    {
        NewMcpName = "";
        NewMcpTransport = McpTransport.Http;
        NewMcpEndpoint = "";
        NewMcpCommand = "";
        NewMcpArgs = "";
        NewMcpAuthType = McpAuthType.None;
        NewMcpAuthToken = "";
        NewMcpAuthHeader = "";
        NewMcpOAuthScopes = "";
        ShowAuthFields = false;
        ShowAddMcp = true;
        NotifyStateChanged();
    }

    public async Task AddMcpConnectionAsync()
    {
        if (!ProjectId.HasValue || string.IsNullOrWhiteSpace(NewMcpName))
            return;
        try
        {
            var conn = await _svc.CreateMcpConnectionAsync(
                new CreateMcpConnectionCommand(
                    ProjectId.Value,
                    NewMcpName,
                    NewMcpTransport,
                    NewMcpTransport != McpTransport.Stdio ? NewMcpEndpoint : null,
                    NewMcpTransport == McpTransport.Stdio ? NewMcpCommand : null,
                    NewMcpTransport == McpTransport.Stdio ? NewMcpArgs : null,
                    NewMcpAuthType != McpAuthType.None ? NewMcpAuthType : null,
                    NewMcpAuthType != McpAuthType.None && NewMcpAuthType != McpAuthType.OAuth
                        ? NewMcpAuthToken
                        : null,
                    NewMcpAuthType == McpAuthType.Header ? NewMcpAuthHeader : null,
                    null,
                    NewMcpAuthType == McpAuthType.OAuth ? NewMcpOAuthScopes : null
                )
            );
            ShowAddMcp = false;
            await LoadMcpConnectionsAsync();
            NotifyStateChanged();
        }
        catch { }
    }

    public async Task TestMcpConnectionAsync(Guid id)
    {
        try
        {
            await _svc.TestMcpConnectionAsync(id);
            await LoadMcpConnectionsAsync();
            NotifyStateChanged();
        }
        catch { }
    }

    public async Task DeleteMcpConnectionAsync(Guid id)
    {
        try
        {
            await _svc.DeleteMcpConnectionAsync(id);
            await LoadMcpConnectionsAsync();
            NotifyStateChanged();
        }
        catch { }
    }

    public string GetOAuthUrl(Guid connectionId) => $"/mcp-oauth/start?connectionId={connectionId}";
}
