using Microsoft.AspNetCore.Authorization;

namespace PlaceContext.Host.Auth;

/// <summary>Authorization requirement for a single Core API scope.</summary>
public sealed class CoreApiScopeRequirement : IAuthorizationRequirement
{
    public CoreApiScopeRequirement(string scope) => Scope = scope;

    public string Scope { get; }
}
