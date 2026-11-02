using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Mcp;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class McpConnectionFormViewModel(PlaceContextService service,
    AuthenticationStateProvider authentication, IAuthorizationService authorization) : PageViewModel, IComponentViewModel
{
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
    public bool Saving { get; private set; }
    public string? Error { get; private set; }

    public async Task<McpConnectionView?> SaveAsync(Guid projectId)
    {
        if (Saving) return null;
        Saving = true; Error = null;
        try
        {
            var user = (await authentication.GetAuthenticationStateAsync()).User;
            if (!(await authorization.AuthorizeAsync(user, Policies.DefaultAdmin)).Succeeded)
                throw new UnauthorizedAccessException("Only workspace administrators can add MCP servers.");
            var connection = await service.CreateMcpConnectionAsync(new(projectId, Name, Transport,
                IsStdio ? null : Endpoint, IsStdio ? Command : null, IsStdio ? Args : null,
                AuthType, HasTokenAuth || IsHeaderAuth ? AuthToken : null, IsHeaderAuth ? AuthHeader : null,
                null, IsOAuth ? OAuthScopes : null));
            AuthToken = "";
            return connection;
        }
        catch (Exception ex) { Error = ex.Message; return null; }
        finally { Saving = false; }
    }
}
