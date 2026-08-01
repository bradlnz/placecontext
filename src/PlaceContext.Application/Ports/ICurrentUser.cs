namespace PlaceContext.Application.Ports;

/// <summary>
/// The authenticated caller behind the current request or Blazor circuit activity — mirrors
/// <see cref="ICurrentTenant"/> (an ambient, AsyncLocal-backed value) so it is correct both inside the
/// dispatcher's per-operation DI scopes and inside later circuit callbacks that don't share the
/// original HTTP request's scope. Populated once ASP.NET Core authorization has resolved the request's
/// principal for whichever scheme applies (cookie for the portal, bearer for MCP) — see
/// <c>UserResolutionMiddleware</c> in the Host project.
/// </summary>
public interface ICurrentUser
{
    Guid UserId { get; }
    /// <summary>The caller's role <em>name</em> — a <c>role_definitions</c> row or a
    /// <see cref="UserRole"/> enum name. Custom roles flow through here as plain names; the coarse
    /// Member/Admin/Owner policies enum-parse claims themselves and intentionally do not match them.</summary>
    string Role { get; }
    bool IsAuthenticated { get; }
}
